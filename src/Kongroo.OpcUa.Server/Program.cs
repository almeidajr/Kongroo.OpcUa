using System.Globalization;
using Kongroo.OpcUa.Server;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
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

// ValidateOnStart turns a malformed port into a refusal to boot. Falling back
// to the default instead would look exactly like a working server on the wrong
// port.
builder
    .Services.AddOptions<PlantServerOptions>()
    .Bind(builder.Configuration.GetSection(PlantServerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Bound eagerly, not through IOptions: the user store is a constructor argument to services
// registered below, so it has to exist before the provider does. A malformed user throws here,
// which is what makes a bad configuration a refusal to boot rather than a silent weak account.
// Named startupPlantOptions, not plantOptions: the pre-existing
// Configure<IOptions<PlantServerOptions>> lambda further down already binds a parameter called
// plantOptions, at the wrapper type. One identifier for two different types in one file invites a
// reader at that lambda to think it is this value.
var startupPlantOptions =
    builder.Configuration.GetSection(PlantServerOptions.SectionName).Get<PlantServerOptions>()
    ?? new PlantServerOptions();

var userDatabase = PlantUsers.CreateUserDatabase(startupPlantOptions.Users);
var userManagement = new UserManagement(
    userDatabase,
    // Fully qualified: ImplicitUsings is enabled repo-wide, so a bare `Range` is ambiguous
    // between Opc.Ua.Range and System.Range (CS0104). Argument order is (high, low), verified
    // by the Task 1 probe: new Opc.Ua.Range(64, 8) yields High=64, Low=8.
    new Opc.Ua.Range(64, PlantUsers.MinimumPasswordLength),
    null,
    null
);

// Registered even when no users are configured. The stack's default-authenticator factory skips
// UserNamePasswordAuthenticator in silence when either service is missing, which would leave the
// endpoint advertising password login and rejecting every password with nothing in the log.
// Registering unconditionally makes that path unreachable; an empty store simply has no accounts.
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
        // Security defaults are already correct: Sign&Encrypt on, None
        // off, SHA-1 rejected. Never add AutoAcceptUntrustedCertificates
        // or IncludeUnsecurePolicyNone here for convenience.
        options.RejectSHA1Certificates = true;
        options.MinCertificateKeySize = 2048;
        options.IncludeSignAndEncryptPolicies = true;
        options.IncludeUnsecurePolicyNone = false;
        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
        options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
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
        options.EnableUserNamePassword = true;
        options.EnableX509 = false;
        options.EnableJwt = false;
    })
    // ConfigureRoles alone registers the role manager; AddRoleManager is only for supplying a
    // custom IRoleManager. RoleManager's constructor already seeds every well-known role, and
    // roles are matched by BrowseName, so these resolve to the standard Part 18 NodeIds rather
    // than creating new ones.
    .ConfigureRoles(roleOptions => PlantUsers.ConfigureRoles(roleOptions, startupPlantOptions.Users))
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
