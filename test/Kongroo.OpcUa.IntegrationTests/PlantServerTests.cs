using Opc.Ua;
using Shouldly;

namespace Kongroo.OpcUa.IntegrationTests;

/// <summary>
/// Drives the in-process server with a real OPC UA client. Every assertion
/// here crosses the wire, so together they prove the address space is
/// browsable, readable, writable and callable — which a unit test on
/// <c>PlantSimulation</c> cannot.
/// </summary>
/// <remarks>
/// The tests share one server through the class fixture and three of them
/// move the setpoint, so they rely on xUnit running the methods of a class
/// sequentially. Do not mark this class for parallel execution.
/// </remarks>
public sealed class PlantServerTests(PlantServerFixture fixture) : IClassFixture<PlantServerFixture>
{
    [Fact]
    public async Task Browse_ShouldFindPlantUnderObjectsFolder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(fixture.EndpointUrl, cancellationToken);

        var children = await client.BrowseChildrenAsync(ObjectIds.ObjectsFolder, cancellationToken);

        children.ShouldContain("Plant");
    }

    [Fact]
    public async Task Read_Temperature_ShouldBeWithinOscillationBandOfSetpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(fixture.EndpointUrl, cancellationToken);

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        var temperature = await client.ReadDoubleAsync("Temperature", cancellationToken);

        temperature.ShouldBeInRange(setpoint - 2.5001, setpoint + 2.5001);
    }

    [Fact]
    public async Task Write_Setpoint_ShouldRoundTrip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(fixture.EndpointUrl, cancellationToken);

        await client.WriteDoubleAsync("Setpoint", 42.0, cancellationToken);

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(42.0);
    }

    [Fact]
    public async Task Write_Setpoint_WithValueAboveMaximum_ShouldClamp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(fixture.EndpointUrl, cancellationToken);

        await client.WriteDoubleAsync("Setpoint", 1000.0, cancellationToken);

        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(95.0);
    }

    [Fact]
    public async Task Call_SetSetpoint_ShouldReturnAcceptedValueAndUpdateVariable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await PlantClient.ConnectAsync(fixture.EndpointUrl, cancellationToken);

        var accepted = await client.CallSetSetpointAsync(1000.0, cancellationToken);

        accepted.ShouldBe(95.0);
        var setpoint = await client.ReadDoubleAsync("Setpoint", cancellationToken);
        setpoint.ShouldBe(95.0);
    }
}
