using Opc.Ua;
using Opc.Ua.Server;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// The Plant instance nodes that carry a <c>RolePermissions</c> attribute.
/// </summary>
internal enum PlantNode
{
    /// <summary>The Plant object, which is also the event notifier.</summary>
    Plant,

    /// <summary>The read-only, historized temperature variable.</summary>
    Temperature,

    /// <summary>The read/write setpoint variable.</summary>
    Setpoint,

    /// <summary>The <c>SetSetpoint</c> method.</summary>
    SetSetpoint,
}

/// <summary>
/// The authorization matrix for the Plant address space, as pure data.
/// </summary>
/// <remarks>
/// <para>
/// Permissions in OPC UA are grant-only: there is no deny bit, and a role is refused by being
/// left out of a node's list. Anonymous therefore appears nowhere, which is the entire denial
/// mechanism for unauthenticated sessions.
/// </para>
/// <para>
/// Every function here fails closed. A role this type does not know returns
/// <see cref="PermissionType.None"/> rather than falling through to a permissive default, because
/// the stack treats an <em>empty</em> permission list as "unrestricted" — so a mistake in this
/// file grants access rather than denying it.
/// </para>
/// </remarks>
internal static class PlantAuthorization
{
    /// <summary>
    /// Permissions <paramref name="role"/> holds on <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The Plant node being described.</param>
    /// <param name="role">
    /// The role being asked about. Any role other than <see cref="Role.Observer"/> and
    /// <see cref="Role.Operator"/> — including <see cref="Role.Anonymous"/> — holds nothing.
    /// </param>
    /// <returns>The permission bits granted, or <see cref="PermissionType.None"/>.</returns>
#pragma warning disable S3358
    internal static PermissionType PermissionsFor(PlantNode node, Role role) =>
        role == Role.Operator ? OperatorPermissionsFor(node)
        : role == Role.Observer ? ObserverPermissionsFor(node)
        : PermissionType.None;
#pragma warning restore S3358

    /// <summary>
    /// The <c>RolePermissions</c> attribute value for <paramref name="node"/>: one entry per role
    /// that holds anything on it.
    /// </summary>
    /// <param name="node">The Plant node being described.</param>
    /// <returns>
    /// Entries for <see cref="Role.Observer"/> and <see cref="Role.Operator"/>. Never empty — an
    /// empty list would mean "unrestricted" to the stack, which is the opposite of the intent.
    /// </returns>
    internal static RolePermissionType[] RolePermissionsFor(PlantNode node) =>
        [RolePermissionFor(node, Role.Observer), RolePermissionFor(node, Role.Operator)];

    private static RolePermissionType RolePermissionFor(PlantNode node, Role role) =>
        new()
        {
            // null! rather than a NamespaceTable: the parameter is declared non-nullable upstream,
            // but a well-known role id is ns=0 with an empty NamespaceUri, so ToNodeId returns at
            // its early path without ever reading the table (and null-checks it even after that).
            // Passing a real table would imply it participates in the conversion; it does not.
            RoleId = ExpandedNodeId.ToNodeId(role.RoleId, null!),
            Permissions = (uint)PermissionsFor(node, role),
        };

    private static PermissionType ObserverPermissionsFor(PlantNode node) =>
        node switch
        {
            PlantNode.Plant => PermissionType.Browse | PermissionType.ReceiveEvents,
            PlantNode.Temperature => PermissionType.Browse | PermissionType.Read | PermissionType.ReadHistory,
            PlantNode.Setpoint => PermissionType.Browse | PermissionType.Read,
            PlantNode.SetSetpoint => PermissionType.Browse,
            _ => PermissionType.None,
        };

    // Operator is Observer plus the two mutating operations, expressed as a delta so the two
    // rows of the matrix cannot drift apart.
    private static PermissionType OperatorPermissionsFor(PlantNode node) =>
        node switch
        {
            PlantNode.Setpoint => ObserverPermissionsFor(node) | PermissionType.Write,
            PlantNode.SetSetpoint => ObserverPermissionsFor(node) | PermissionType.Call,
            _ => ObserverPermissionsFor(node),
        };
}
