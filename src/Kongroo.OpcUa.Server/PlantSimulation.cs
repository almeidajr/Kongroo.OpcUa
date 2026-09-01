namespace Kongroo.OpcUa.Server;

/// <summary>
/// The plant's mutable state, held as one immutable record so it can be
/// swapped atomically without a lock.
/// </summary>
/// <param name="Setpoint">Target temperature in degrees Celsius.</param>
/// <param name="EpochTicks">
/// UTC tick count the simulation measures elapsed time from.
/// </param>
internal sealed record PlantSimulationState(double Setpoint, long EpochTicks);

/// <summary>
/// Pure simulation logic. Time arrives as a parameter rather than being
/// read from a clock, which is what makes every function here testable
/// with no fake and no ceremony.
/// </summary>
internal static class PlantSimulation
{
    /// <summary>Setpoint the server starts at, in degrees Celsius.</summary>
    internal const double InitialSetpoint = 21.0;

    /// <summary>Lowest setpoint a client may set, in degrees Celsius.</summary>
    internal const double MinimumSetpoint = 5.0;

    /// <summary>Highest setpoint a client may set, in degrees Celsius.</summary>
    internal const double MaximumSetpoint = 95.0;

    private const double OscillationAmplitude = 2.5;
    private const double OscillationPeriodSeconds = 60.0;

    /// <summary>
    /// Temperature at <paramref name="nowUtc"/>: the setpoint plus a
    /// 60-second sine oscillation of +/- 2.5 degrees, so a subscribing
    /// client sees motion immediately and history accumulates a visible
    /// waveform.
    /// </summary>
    internal static double TemperatureAt(PlantSimulationState state, DateTimeOffset nowUtc)
    {
        var elapsedSeconds = (nowUtc.UtcTicks - state.EpochTicks) / (double)TimeSpan.TicksPerSecond;

        return state.Setpoint
            + (OscillationAmplitude * Math.Sin(elapsedSeconds * 2.0 * Math.PI / OscillationPeriodSeconds));
    }

    /// <summary>
    /// Constrains a client-supplied setpoint to the valid range. This is a
    /// trust boundary: a client may write any <see cref="double"/>,
    /// including NaN and the infinities, so the value is validated rather
    /// than trusted. NaN has no meaningful clamp, so it resolves to
    /// <see cref="InitialSetpoint"/> instead of propagating.
    /// </summary>
    internal static double ClampSetpoint(double requested) =>
        double.IsNaN(requested) ? InitialSetpoint : Math.Clamp(requested, MinimumSetpoint, MaximumSetpoint);
}
