using System.ComponentModel.DataAnnotations;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Server settings bound from the <c>OpcUa</c> configuration section. The
/// security toggles are deliberately absent so that no configuration file can
/// weaken the endpoint's security posture.
/// </summary>
/// <remarks>
/// Bound with <c>ValidateDataAnnotations().ValidateOnStart()</c>, so a value
/// outside its declared range aborts startup instead of falling back to the
/// default. Validation is declared with data annotations rather than a
/// hand-written predicate, so a setting added later is validated by the
/// existing call without touching <c>Program.cs</c>.
/// </remarks>
internal sealed record PlantServerOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    internal const string SectionName = "OpcUa";

    /// <summary>Port used when configuration supplies none.</summary>
    internal const int DefaultPort = 62552;

    /// <summary>Lowest port number a TCP endpoint may bind.</summary>
    internal const int MinimumPort = 1;

    /// <summary>Highest port number a TCP endpoint may bind.</summary>
    internal const int MaximumPort = 65535;

    /// <summary>TCP port the <c>opc.tcp</c> endpoint binds.</summary>
    /// <value>
    /// A port between <see cref="MinimumPort"/> and <see cref="MaximumPort"/>;
    /// <see cref="DefaultPort"/> when configuration supplies none. Anything
    /// outside that range fails validation and the host refuses to start.
    /// </value>
    [Range(MinimumPort, MaximumPort)]
    public int Port { get; init; } = DefaultPort;

    /// <summary>Users seeded into the in-memory store at startup.</summary>
    /// <value>
    /// Empty when configuration supplies none, which is legal: the server boots, only anonymous
    /// sessions can connect, and the Plant is invisible to every one of them.
    /// </value>
    public IList<PlantUserOptions> Users { get; } = [];
}

/// <summary>
/// Role a seeded user is granted. Maps to the well-known Part 18 roles;
/// it exists as a separate enum because <see cref="Opc.Ua.Server.Role"/>
/// cannot be bound from configuration.
/// </summary>
internal enum PlantRole
{
    /// <summary>Reads <c>Temperature</c> and <c>Setpoint</c>, and receives events.</summary>
    Observer,

    /// <summary>Everything <see cref="Observer"/> may do, plus writing
    /// <c>Setpoint</c> and calling <c>SetSetpoint</c>.</summary>
    Operator,
}

/// <summary>
/// One user seeded into the in-memory user store at startup.
/// </summary>
/// <remarks>
/// These are validated by <c>PlantUsers.CreateUserDatabase</c>, not by data annotations:
/// <c>ValidateDataAnnotations</c> inspects only the top-level properties of an options object and
/// does not recurse into collection items, so attributes here would never run.
/// </remarks>
internal sealed record PlantUserOptions
{
    /// <summary>User name presented in the OPC UA identity token. Never blank.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Password in clear text, supplied through user secrets or environment variables.
    /// Never committed to a configuration file. Hashed with PBKDF2-SHA512 on seeding.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Role this user is granted.</summary>
    public PlantRole Role { get; init; }
}
