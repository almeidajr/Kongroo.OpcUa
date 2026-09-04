using Kongroo.OpcUa.Server;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

/// <summary>
/// Covers the pure simulation: the waveform at the four cardinal points of
/// one oscillation period, and every branch of the setpoint trust boundary.
/// </summary>
public sealed class PlantSimulationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static PlantSimulationState StateAt(double setpoint) => new(setpoint, Epoch.UtcTicks);

    [Fact]
    public void TemperatureAt_WhenAtEpoch_ShouldEqualSetpoint()
    {
        var temperature = PlantSimulation.TemperatureAt(StateAt(21.0), Epoch);

        temperature.ShouldBe(21.0, tolerance: 1e-9);
    }

    [Fact]
    public void TemperatureAt_WhenQuarterPeriodElapsed_ShouldReachUpperBound()
    {
        var temperature = PlantSimulation.TemperatureAt(StateAt(21.0), Epoch.AddSeconds(15.0));

        temperature.ShouldBe(23.5, tolerance: 1e-9);
    }

    [Fact]
    public void TemperatureAt_WhenThreeQuarterPeriodElapsed_ShouldReachLowerBound()
    {
        var temperature = PlantSimulation.TemperatureAt(StateAt(21.0), Epoch.AddSeconds(45.0));

        temperature.ShouldBe(18.5, tolerance: 1e-9);
    }

    [Fact]
    public void TemperatureAt_WhenFullPeriodElapsed_ShouldReturnToSetpoint()
    {
        var temperature = PlantSimulation.TemperatureAt(StateAt(21.0), Epoch.AddSeconds(60.0));

        temperature.ShouldBe(21.0, tolerance: 1e-9);
    }

    [Fact]
    public void TemperatureAt_WithChangedSetpoint_ShouldOscillateAroundNewSetpoint()
    {
        var temperature = PlantSimulation.TemperatureAt(StateAt(40.0), Epoch.AddSeconds(15.0));

        temperature.ShouldBe(42.5, tolerance: 1e-9);
    }

    [Fact]
    public void ClampSetpoint_WithValueInRange_ShouldReturnItUnchanged() =>
        PlantSimulation.ClampSetpoint(30.0).ShouldBe(30.0);

    [Fact]
    public void ClampSetpoint_WithValueAboveMaximum_ShouldClampToMaximum() =>
        PlantSimulation.ClampSetpoint(1000.0).ShouldBe(PlantSimulation.MaximumSetpoint);

    [Fact]
    public void ClampSetpoint_WithValueBelowMinimum_ShouldClampToMinimum() =>
        PlantSimulation.ClampSetpoint(-40.0).ShouldBe(PlantSimulation.MinimumSetpoint);

    [Fact]
    public void ClampSetpoint_WithPositiveInfinity_ShouldClampToMaximum() =>
        PlantSimulation.ClampSetpoint(double.PositiveInfinity).ShouldBe(PlantSimulation.MaximumSetpoint);

    [Fact]
    public void ClampSetpoint_WithNegativeInfinity_ShouldClampToMinimum() =>
        PlantSimulation.ClampSetpoint(double.NegativeInfinity).ShouldBe(PlantSimulation.MinimumSetpoint);

    [Fact]
    public void ClampSetpoint_WithNotANumber_ShouldReturnInitialSetpoint() =>
        PlantSimulation.ClampSetpoint(double.NaN).ShouldBe(PlantSimulation.InitialSetpoint);
}
