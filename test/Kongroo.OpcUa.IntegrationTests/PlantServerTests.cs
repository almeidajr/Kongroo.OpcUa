using System.Text;
using Opc.Ua;
using Shouldly;

namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// Drives the in-process server with a real OPC UA client. Every assertion
/// here crosses the wire, so together they prove the address space is
/// browsable, readable, writable, callable and observable — which a unit
/// test on <c>PlantSimulation</c> cannot.
/// </summary>
/// <remarks>
/// <para>
/// The tests share one server through the class fixture and all but the
/// browse test move the setpoint, so they rely on xUnit running the methods
/// of a class sequentially. Do not mark this class for parallel execution.
/// </para>
/// <para>
/// <c>Temperature</c> is wired with <c>PollEvery(1s)</c> and no
/// <c>OnRead</c>, so a read returns the node's cached sample — up to a
/// second stale, and therefore lagging a setpoint change. <c>Setpoint</c>
/// has an <c>OnRead</c> and is instantaneous. Anything comparing the two has
/// to poll for the lag: today the ~2.4 s each client connect spends on PKI
/// and the handshake happens to exceed one poll interval, but that margin is
/// incidental and hoisting the client into the fixture would remove it.
/// </para>
/// </remarks>
public sealed class PlantServerTests(PlantServerFixture fixture) : IClassFixture<PlantServerFixture>
{
    /// <summary>
    /// The simulation's oscillation amplitude, plus a rounding margin.
    /// </summary>
    private const double OscillationBand = 2.5001;

    /// <summary>Lower bound of the simulation's setpoint clamp range.</summary>
    private const double PlantSimulationMinimum = 5.0;

    /// <summary>Upper bound of the simulation's setpoint clamp range.</summary>
    private const double PlantSimulationMaximum = 95.0;

    private static readonly TimeSpan TemperatureSettleTimeout = TimeSpan.FromSeconds(10.0);
    private static readonly TimeSpan TemperaturePollInterval = TimeSpan.FromMilliseconds(200.0);
    private static readonly TimeSpan EventDeliveryTimeout = TimeSpan.FromSeconds(30.0);
    private static readonly TimeSpan EventWriteRetryInterval = TimeSpan.FromMilliseconds(200.0);

    [Fact]
    public async Task Browse_ShouldFindPlantUnderObjectsFolder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );

        var children = await client.BrowseChildrenAsync(ObjectIds.ObjectsFolder, cancellationToken);

        children.ShouldContain("Plant");
    }

    [Fact]
    public async Task Read_Temperature_ShouldBeWithinOscillationBandOfSetpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );
        const double setpoint = 30.0;

        // Writes its own setpoint rather than reading whichever value a
        // previous test left behind: Setpoint reads instantly and Temperature
        // does not, so a comparison against someone else's setpoint is a race.
        await client.WriteDoubleAsync("Setpoint", setpoint, cancellationToken);

        var temperature = await ReadSettledTemperatureAsync(client, setpoint, cancellationToken);

        temperature.ShouldBeInRange(setpoint - OscillationBand, setpoint + OscillationBand);
    }

    [Fact]
    public async Task Write_Setpoint_ShouldRoundTrip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );

        await client.WriteDoubleAsync("Setpoint", 42.0, cancellationToken);

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(42.0);
    }

    [Fact]
    public async Task Write_Setpoint_WithValueAboveMaximum_ShouldClamp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );

        await client.WriteDoubleAsync("Setpoint", 1000.0, cancellationToken);

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(95.0);
    }

    [Fact]
    public async Task Call_SetSetpoint_ShouldReturnAcceptedValueAndUpdateVariable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );

        var accepted = await client.CallSetSetpointAsync(1000.0, cancellationToken);

        accepted.ShouldBe(95.0);
        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(95.0);
    }

    [Fact]
    public async Task Subscribe_SetpointChanges_ShouldDeliverOnlyChangesMadeWhileSubscribed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.OperatorIdentity,
            cancellationToken
        );
        const double unsubscribedSetpoint = 55.0;
        const double subscribedSetpoint = 30.0;

        // Nobody is subscribed, so the server's event stream is not running
        // yet and this change only reaches its channel. Activating the stream
        // drains that backlog, so this value must never be delivered below.
        await client.WriteDoubleAsync("Setpoint", unsubscribedSetpoint, cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(EventDeliveryTimeout);
        await using var changes = client
            .SubscribeSetpointChangesAsync(deadline.Token)
            .GetAsyncEnumerator(deadline.Token);
        var firstChange = changes.MoveNextAsync().AsTask();

        // The monitored item is created asynchronously once enumeration starts,
        // so an early write can still land in the backlog the drain discards.
        // Re-writing until one is delivered is what makes this deterministic
        // instead of sleeping and hoping the subscription is live.
        while (!firstChange.IsCompleted && !deadline.IsCancellationRequested)
        {
            await client.WriteDoubleAsync("Setpoint", subscribedSetpoint, cancellationToken);
            await Task.Delay(EventWriteRetryInterval, cancellationToken);
        }

        firstChange.IsCompleted.ShouldBeTrue($"No setpoint-changed event arrived within {EventDeliveryTimeout}.");
        (await firstChange).ShouldBeTrue("The event stream ended before delivering a setpoint change.");
        // The drain: the pre-subscription change is not replayed.
        changes.Current.NewSetpoint.ShouldNotBe(unsubscribedSetpoint);
        // The delivery: a change made while subscribed arrives, with its value.
        changes.Current.NewSetpoint.ShouldBe(subscribedSetpoint);
    }

    [Fact]
    public async Task Connect_WithWrongPassword_ShouldFailToActivateTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrongCredentials = new UserIdentity(
            PlantServerFixture.OperatorUserName,
            Encoding.UTF8.GetBytes("wrong-password")
        );

        await Should.ThrowAsync<ServiceResultException>(async () =>
            await PlantClient.ConnectAsync(fixture.EndpointUrl, wrongCredentials, cancellationToken)
        );
    }

    [Fact]
    public async Task Connect_WithAnUnknownUserName_ShouldFailToActivateTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unknownUser = new UserIdentity("nobody", Encoding.UTF8.GetBytes("some-password"));

        await Should.ThrowAsync<ServiceResultException>(async () =>
            await PlantClient.ConnectAsync(fixture.EndpointUrl, unknownUser, cancellationToken)
        );
    }

    [Fact]
    public async Task Connect_WithObserverCredentials_ShouldActivateTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var client = await PlantClient.ConnectAsync(
            fixture.EndpointUrl,
            PlantServerFixture.ObserverIdentity,
            cancellationToken
        );

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBeInRange(PlantSimulationMinimum, PlantSimulationMaximum);
    }

    /// <summary>
    /// Reads <c>Temperature</c> until its cached sample reflects
    /// <paramref name="setpoint"/> or the deadline passes. Returns the last
    /// value read either way, so a timeout fails the caller's assertion with
    /// the stale reading rather than passing quietly.
    /// </summary>
    private static async Task<double> ReadSettledTemperatureAsync(
        PlantClient client,
        double setpoint,
        CancellationToken cancellationToken
    )
    {
        using var deadline = new CancellationTokenSource(TemperatureSettleTimeout);

        while (true)
        {
            var temperature = await client.ReadDoubleAsync("Temperature", cancellationToken);

            if (Math.Abs(temperature - setpoint) <= OscillationBand || deadline.IsCancellationRequested)
            {
                return temperature;
            }

            await Task.Delay(TemperaturePollInterval, cancellationToken);
        }
    }
}
