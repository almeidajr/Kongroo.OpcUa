using System.ComponentModel.DataAnnotations;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Server settings bound from the <c>OpcUa</c> configuration section. The
/// security toggles are deliberately absent so that no configuration file can
/// weaken the endpoint's security posture.
/// </summary>
/// <remarks>
/// Validation is declared with data annotations rather than a hand-written
/// predicate, so a setting added later is validated by the existing
/// <c>ValidateDataAnnotations</c> call without touching <c>Program.cs</c>.
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
    [Range(MinimumPort, MaximumPort)]
    public int Port { get; init; } = DefaultPort;
}
