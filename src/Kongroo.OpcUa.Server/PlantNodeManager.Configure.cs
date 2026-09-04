using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Opc.Ua;
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
        // Must stay the FIRST statement: PollEvery primes the node by
        // invoking the sample lambda synchronously at wiring time, so an
        // epoch stamped after the wiring block would prime the node from
        // EpochTicks = 0 — a first sample dated year 1.
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
        // OnRead is load-bearing here, not a convenience: once the write
        // handler returns Good the stack commits the client's RAW written
        // value into the node's cached Value, so without OnRead a client
        // reads back its own unclamped number instead of the clamp.
        builder.Plant.Setpoint.OnRead(() => _state.Setpoint).OnWrite(requested => ApplySetpoint(requested));

        // Returns the accepted value so a client can observe the clamp
        // without reading the variable back. The block lambda is not
        // redundant: IDE0200/IDE0350 demand the method group
        // OnCall(ApplySetpoint), and Sonar S3241 then flags ApplySetpoint's
        // return value as unused. This shape satisfies both, no suppression.
        builder.Plant.SetSetpoint.OnCall(requested =>
        {
            var accepted = ApplySetpoint(requested);
            return accepted;
        });

        // Starts lazily on the first event subscriber and cancels on the
        // last, which is why the source is a long-lived channel rather
        // than per-subscription state.
        builder.Plant.Publish(SetpointChangesAsync);
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

        // ponytail: the CAS above and the TryWrite below are two separate
        // atomic steps, so concurrent writers can publish events in an order
        // that differs from the final state — A(30) and B(40) may CAS A then B
        // yet queue B then A, leaving the last event saying 30 while Setpoint
        // reads 40. Accepted ceiling: every event is individually true and
        // clients read current values from the variable. Serialize the two
        // steps only if event order ever has to match state order.

        // Deliberately fire-and-forget: with DropOldest a full channel never
        // blocks or rejects this write, it evicts the oldest queued value
        // instead, so a slow or absent event consumer can never fail it.
        _setpointChanges.Writer.TryWrite(accepted);

        return accepted;
    }

    /// <summary>
    /// Streams one <c>SetpointChangedEventType</c> per accepted setpoint.
    /// The event registry populates EventId, EventType, SourceNode,
    /// SourceName, Time and ReceiveTime, so only NewSetpoint is set here.
    /// </summary>
    private async IAsyncEnumerable<SetpointChangedEventState> SetpointChangesAsync(
        BaseObjectState notifier,
        ISystemContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // Discard anything buffered while no client was subscribed. An OPC UA event
        // subscription delivers changes that occur after it starts; replaying a stale
        // backlog would stamp old changes with a current Time/ReceiveTime.
        while (_setpointChanges.Reader.TryRead(out _))
        {
            // Intentionally empty: draining for its side effect.
        }

        await foreach (var setpoint in _setpointChanges.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var change = new SetpointChangedEventState(parent: notifier);
            change.NewSetpoint = PropertyState<double>.With<VariantBuilder>(change, setpoint);
            yield return change;
        }
    }
}
