using System.Globalization;
using Kongroo.OpcUa.Server;
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
var port = int.TryParse(builder.Configuration["OpcUa:Port"], out var configuredPort) ? configuredPort : 62552;

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
        options.EndpointUrls.Add($"opc.tcp://localhost:{port}/{applicationName}");
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

var host = builder.Build();

await host.RunAsync();
