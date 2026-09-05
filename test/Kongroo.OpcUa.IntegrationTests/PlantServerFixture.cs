using System.Net;
using System.Net.Sockets;
using System.Text;
using Kongroo.OpcUa.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// Boots the real server in-process on a free port and publishes the
/// endpoint the tests connect to. Auto-accepting untrusted certificates is
/// acceptable here because this is test-only code; production
/// <c>Program.cs</c> keeps the stack's secure defaults.
/// </summary>
/// <remarks>
/// One server is shared by every test in the class that takes the fixture, so
/// tests see each other's writes. State that must not leak between tests has
/// to be set by the test itself.
/// </remarks>
public sealed class PlantServerFixture : IAsyncLifetime
{
    /// <summary>
    /// Generous: the very first boot has to create the server's
    /// application-instance certificate before it can open a listener.
    /// </summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    internal const string ObserverUserName = "observer";
    private const string ObserverPassword = "observer-password";
    internal const string OperatorUserName = "operator";
    private const string OperatorPassword = "operator-password";

    private IHost? _host;
    private string _pkiRoot = string.Empty;

    /// <summary>Identity of a user holding only the well-known Observer role.</summary>
    public static UserIdentity ObserverIdentity { get; } =
        new(ObserverUserName, Encoding.UTF8.GetBytes(ObserverPassword));

    /// <summary>Identity of a user holding the well-known Operator role.</summary>
    public static UserIdentity OperatorIdentity { get; } =
        new(OperatorUserName, Encoding.UTF8.GetBytes(OperatorPassword));

    /// <summary>
    /// The <c>opc.tcp</c> endpoint the in-process server listens on.
    /// </summary>
    /// <value>
    /// A loopback URL carrying the port picked for this run, or an empty
    /// string before <see cref="InitializeAsync"/> has completed.
    /// </value>
    public string EndpointUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Starts the server and returns once it is accepting connections.
    /// </summary>
    /// <returns>
    /// A task that completes when the endpoint listeners are bound, so a
    /// client may connect as soon as it is awaited.
    /// </returns>
    /// <exception cref="TimeoutException">
    /// The server did not open its listeners within <see cref="StartTimeout"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The host shut down while starting, which is how a failed boot surfaces
    /// instead of waiting out the timeout.
    /// </exception>
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

        var seededUsers = new PlantUserOptions[]
        {
            new()
            {
                Name = ObserverUserName,
                Password = ObserverPassword,
                Role = PlantRole.Observer,
            },
            new()
            {
                Name = OperatorUserName,
                Password = OperatorPassword,
                Role = PlantRole.Operator,
            },
        };

        var userDatabase = PlantUsers.CreateUserDatabase(seededUsers);
        var userManagement = new UserManagement(
            userDatabase,
            // Fully qualified for the same CS0104 reason as Program.cs: ImplicitUsings is on and
            // this file already has `using Opc.Ua;`. Order is (high, low).
            new Opc.Ua.Range(64, PlantUsers.MinimumPasswordLength),
            null,
            null
        );
        builder.Services.AddSingleton<IUserDatabase>(userDatabase);
        builder.Services.AddSingleton<IUserManagement>(userManagement);

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
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                options.EndpointUrls.Add(EndpointUrl);
            })
            .AddDefaultIdentityAuthenticators(options =>
            {
                options.EnableAnonymous = true;
                options.EnableUserNamePassword = true;
                options.EnableX509 = false;
                options.EnableJwt = false;
            })
            // ConfigureRoles alone registers the role manager; AddRoleManager is only for
            // supplying a custom IRoleManager. Mirrors Program.cs, reading from the users seeded
            // above instead of configuration.
            .ConfigureRoles(roleOptions => PlantUsers.ConfigureRoles(roleOptions, seededUsers))
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
    /// <returns>
    /// A task that completes once the host has stopped; store removal is best
    /// effort and never faults it.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        TestPkiRoot.Delete(_pkiRoot);
    }

    /// <summary>
    /// Asks the OS for an unused loopback port. Inherently racy — nothing
    /// holds the port between this call and the server binding it — but it
    /// keeps concurrent runs off a fixed port.
    /// </summary>
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

        /// <summary>Completes when the server's endpoints are listening.</summary>
        internal Task Started => _started.Task;

        /// <inheritdoc />
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
