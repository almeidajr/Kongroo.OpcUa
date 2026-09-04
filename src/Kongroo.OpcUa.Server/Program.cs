using System.Globalization;
using Kongroo.OpcUa.Server;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(configuration =>
    configuration
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithEnvironmentUserName()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithProcessName()
        .Enrich.WithThreadId()
        .Enrich.WithThreadName()
        .Enrich.WithProperty("Application", AppDomain.CurrentDomain.FriendlyName)
);

builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(static tracing => tracing.AddHttpClientInstrumentation().AddOtlpExporter())
    .WithMetrics(static metrics =>
        metrics.AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddOtlpExporter()
    );

builder.Services.AddSingleton(TimeProvider.System);

const string applicationName = "KongrooOpcUaServer";

// ValidateOnStart turns a malformed port into a refusal to boot. The previous
// int.TryParse fallback silently started on the default instead, which looks
// exactly like a working server on the wrong port.
builder
    .Services.AddOptions<PlantServerOptions>()
    .Bind(builder.Configuration.GetSection(PlantServerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder
    .Services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = applicationName;
        options.ApplicationUri = $"urn:localhost:{applicationName}";
        options.ProductUri = "uri:kongroo.dev:KongrooOpcUaServer";
        options.SubjectName = $"CN={applicationName}, DC=localhost";
        // Security defaults are already correct: Sign&Encrypt on, None
        // off, SHA-1 rejected. Never add AutoAcceptUntrustedCertificates
        // or IncludeUnsecurePolicyNone here for convenience.
        options.RejectSHA1Certificates = true;
        options.MinCertificateKeySize = 2048;
        options.IncludeSignAndEncryptPolicies = true;
        options.IncludeUnsecurePolicyNone = false;
        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
        // Not the stack's default %TEMP%/OPC Foundation/{App}/pki: %TEMP% is
        // routinely cleared, and a server that loses its certificate store
        // regenerates its identity on the next boot, forcing every client to
        // re-trust it.
        options.PkiRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kongroo",
            "OpcUaServer",
            "pki"
        );
    })
    .AddDefaultIdentityAuthenticators(options =>
    {
        // AddServer does NOT auto-wire identity options; without this call
        // authentication is silently absent.
        options.EnableAnonymous = true;
        options.EnableUserNamePassword = false;
        options.EnableX509 = false;
        options.EnableJwt = false;
    })
    .AddNodeManager<PlantNodeManagerFactory>();

// AddServer's callback is an Action<OpcUaServerOptions> with no access to DI,
// so the one setting that comes from configuration is applied in a second,
// dependency-aware Configure. Configure actions run in registration order, so
// this lands after the block above.
builder
    .Services.AddOptions<OpcUaServerOptions>()
    .Configure<IOptions<PlantServerOptions>>(
        (serverOptions, plantOptions) =>
            serverOptions.EndpointUrls.Add($"opc.tcp://localhost:{plantOptions.Value.Port}/{applicationName}")
    );

var host = builder.Build();

await host.RunAsync();
