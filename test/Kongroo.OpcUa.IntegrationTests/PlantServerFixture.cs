using System.Net;
using System.Net.Sockets;
using Kongroo.OpcUa.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// Boots the real server in-process on a free port and publishes the
/// endpoint the tests connect to. Auto-accepting untrusted certificates is
/// acceptable here because this is test-only code; production
/// <c>Program.cs</c> keeps the stack's secure defaults.
/// </summary>
public sealed class PlantServerFixture : IAsyncLifetime
{
    /// <summary>
    /// Generous: the very first boot has to create the server's
    /// application-instance certificate before it can open a listener.
    /// </summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private IHost? _host;
    private string _pkiRoot = string.Empty;

    /// <summary>
    /// The <c>opc.tcp</c> endpoint the in-process server listens on.
    /// </summary>
    public string EndpointUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Starts the server and returns once it is accepting connections.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        const string applicationName = "KongrooOpcUaServer";
        var port = FindFreePort();
        _pkiRoot = TestPkiRoot.Create();
        EndpointUrl = $"opc.tcp://localhost:{port}/{applicationName}";

        var readySignal = new ServerReadySignal();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IServerStartupTask>(readySignal);

        builder
            .Services.AddOpcUa()
            .AddServer(options =>
            {
                options.ApplicationName = applicationName;
                options.ApplicationUri = $"urn:localhost:{applicationName}";
                options.ProductUri = "uri:kongroo.dev:KongrooOpcUaServer";
                options.SubjectName = $"CN={applicationName}, DC=localhost";
                options.PkiRoot = _pkiRoot;
                // Test-only: lets the ephemeral client certificate connect
                // without an out-of-band trust step.
                options.AutoAcceptUntrustedCertificates = true;
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
                options.EndpointUrls.Add(EndpointUrl);
            })
            .AddDefaultIdentityAuthenticators(options =>
            {
                options.EnableAnonymous = true;
                options.EnableUserNamePassword = false;
                options.EnableX509 = false;
                options.EnableJwt = false;
            })
            .AddNodeManager<PlantNodeManagerFactory>();

        _host = builder.Build();
        await _host.StartAsync();

        // The server is a BackgroundService, so StartAsync returns long
        // before a listener exists. Waiting on the host's stopping token as
        // well surfaces a failed boot immediately instead of after the
        // timeout.
        var lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
        await readySignal.Started.WaitAsync(StartTimeout, lifetime.ApplicationStopping);
    }

    /// <summary>
    /// Stops the server and removes its ephemeral certificate store.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        TestPkiRoot.Delete(_pkiRoot);
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Completes once the hosted server has opened its endpoints. The stack
    /// runs every registered <see cref="IServerStartupTask"/> immediately
    /// after the listeners are bound, which makes this a real readiness
    /// signal rather than a guess at how long a boot takes.
    /// </summary>
    private sealed class ServerReadySignal : IServerStartupTask
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => _started.Task;

        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
