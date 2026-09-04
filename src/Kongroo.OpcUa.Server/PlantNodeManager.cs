using Opc.Ua.Server.Fluent;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Exposes the Plant address space in the <c>http://kongroo.dev/UA/Plant/</c>
/// namespace: a read-only <c>Temperature</c>, a read/write <c>Setpoint</c>, a
/// <c>SetSetpoint</c> method, and a <c>SetpointChangedEventType</c> event
/// raised whenever the setpoint is accepted.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[NodeManager]</c> attribute opts this partial class in to source
/// generation. The generator emits the sibling partial deriving from
/// <see cref="FluentNodeManagerBase"/>, which loads the predefined nodes from
/// <c>Model/Plant.xml</c> and then calls the hand-written <c>Configure</c>
/// partial in <c>PlantNodeManager.Configure.cs</c>, where per-node behaviour
/// is wired.
/// </para>
/// <para>
/// Instances are created by the generated <see cref="PlantNodeManagerFactory"/>
/// and owned by the server, not by dependency injection; nothing here is
/// resolved from a service provider.
/// </para>
/// </remarks>
[NodeManager(NamespaceUri = "http://kongroo.dev/UA/Plant/")]
public sealed partial class PlantNodeManager;
