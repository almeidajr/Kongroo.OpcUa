using System.Collections.Immutable;
using System.Threading.Channels;
using Opc.Ua.Server.Fluent;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Behaviour wiring for <see cref="PlantNodeManager"/>. Runs once at
/// startup, after the generated base has materialized the predefined
/// Plant instance, so every browse path below is addressable here.
/// </summary>
/// <remarks>
/// Wiring resolves eagerly: a bad browse path throws on boot rather than
/// on a client's first read. That is deliberate — do not wrap
/// <c>Configure</c> in a try/catch.
/// </remarks>
public sealed partial class PlantNodeManager
{
    /// <summary>
    /// Accepted setpoint values awaiting publication as events. Bounded
    /// and dropping oldest, so an absent or slow event consumer can never
    /// stall a client's write.
    /// </summary>
    private readonly Channel<double> _setpointChanges = Channel.CreateBounded<double>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest }
    );

    // ponytail: a plain TimeProvider field, not DI-injected — the node
    // manager is not DI-activated (only the factory is) so injecting one
    // means subclassing the generated factory and duplicating its
    // construction logic. Override the generated factory's virtual
    // CreateAsync if an in-process fake clock is ever actually needed.
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private PlantSimulationState _state = new(PlantSimulation.InitialSetpoint, EpochTicks: 0);

    partial void Configure(IPlantNodeManagerBuilder builder)
    {
        // Stamped here rather than in the field initializer: a field
        // initializer cannot reference an instance field (CS0236).
        // Configure runs once at startup before any client exists, so a
        // plain assignment is safe; every later mutation is atomic.
        _state = _state with
        {
            EpochTicks = _timeProvider.GetUtcNow().UtcTicks,
        };

        // PollEvery pushes on change and applies an initial sample
        // immediately, so it REPLACES OnRead rather than accompanying it.
        // Wiring both would double-wire the node (BadConfigurationError).
        // The bare Historize() lazily installs an in-memory historian.
        builder
            .Plant.Temperature.PollEvery(
                TimeSpan.FromSeconds(1.0),
                () => PlantSimulation.TemperatureAt(_state, _timeProvider.GetUtcNow())
            )
            .Historize();

        // OnRead and OnWrite are distinct operations, so they coexist.
        builder.Plant.Setpoint.OnRead(() => _state.Setpoint).OnWrite(requested => ApplySetpoint(requested));

        // Returns the accepted value so a client can observe the clamp
        // without reading the variable back.
        builder.Plant.SetSetpoint.OnCall(requested =>
        {
            var accepted = ApplySetpoint(requested);
            return accepted;
        });
    }

    /// <summary>
    /// Validates and applies a client-supplied setpoint, then offers the
    /// accepted value for event publication. Returns the value applied.
    /// </summary>
    private double ApplySetpoint(double requested)
    {
        var accepted = PlantSimulation.ClampSetpoint(requested);

        ImmutableInterlocked.Update(
            ref _state,
            static (state, setpoint) => state with { Setpoint = setpoint },
            accepted
        );

        // Deliberately fire-and-forget: DropOldest means TryWrite only
        // fails if no reader has ever drained, which must not fail a write.
        _setpointChanges.Writer.TryWrite(accepted);

        return accepted;
    }
}
