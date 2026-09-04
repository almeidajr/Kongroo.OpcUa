using Opc.Ua.Server.Fluent;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Source-generated node manager for the Plant model. The
/// <c>[NodeManager]</c> attribute opts this partial class in to source
/// generation: the generator emits the sibling partial that derives from
/// <c>FluentNodeManagerBase</c>, owns the predefined-node load, and calls
/// back into the <c>Configure</c> partial in
/// <c>PlantNodeManager.Configure.cs</c>.
/// </summary>
[NodeManager(NamespaceUri = "http://kongroo.dev/UA/Plant/")]
public sealed partial class PlantNodeManager;
