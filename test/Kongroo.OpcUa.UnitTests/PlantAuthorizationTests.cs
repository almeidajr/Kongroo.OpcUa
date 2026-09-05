using Kongroo.OpcUa.Server;
using Opc.Ua;
using Opc.Ua.Server;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

/// <summary>
/// Covers the authorization matrix. These assertions matter more than most: an absent or
/// mistyped permission bit fails <em>open</em>, because the stack treats an empty permission
/// list as "no restriction".
/// </summary>
public sealed class PlantAuthorizationTests
{
    [Fact]
    public void PermissionsFor_WithObserverRole_ShouldNotGrantWriteOnSetpoint() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Setpoint, Role.Observer)
            .HasFlag(PermissionType.Write)
            .ShouldBeFalse();

    [Fact]
    public void PermissionsFor_WithOperatorRole_ShouldGrantWriteOnSetpoint() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Setpoint, Role.Operator)
            .HasFlag(PermissionType.Write)
            .ShouldBeTrue();

    [Fact]
    public void PermissionsFor_WithObserverRole_ShouldGrantReadOnSetpoint() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Setpoint, Role.Observer)
            .HasFlag(PermissionType.Read)
            .ShouldBeTrue();

    [Fact]
    public void PermissionsFor_WithOperatorRole_ShouldGrantCallOnSetSetpoint() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.SetSetpoint, Role.Operator)
            .HasFlag(PermissionType.Call)
            .ShouldBeTrue();

    [Fact]
    public void PermissionsFor_WithObserverRole_ShouldNotGrantCallOnSetSetpoint() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.SetSetpoint, Role.Observer)
            .HasFlag(PermissionType.Call)
            .ShouldBeFalse();

    [Fact]
    public void PermissionsFor_WithObserverRole_ShouldGrantReadHistoryOnTemperature() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Temperature, Role.Observer)
            .HasFlag(PermissionType.ReadHistory)
            .ShouldBeTrue();

    [Fact]
    public void PermissionsFor_WithObserverRole_ShouldNotGrantWriteOnTemperature() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Temperature, Role.Observer)
            .HasFlag(PermissionType.Write)
            .ShouldBeFalse();

    [Fact]
    public void PermissionsFor_WithOperatorRole_ShouldNotGrantWriteOnTemperature() =>
        PlantAuthorization
            .PermissionsFor(PlantNode.Temperature, Role.Operator)
            .HasFlag(PermissionType.Write)
            .ShouldBeFalse();

    [Fact]
    public void PermissionsFor_WithAnonymousRole_ShouldGrantNothingOnEveryNode()
    {
        foreach (var node in Enum.GetValues<PlantNode>())
        {
            PlantAuthorization.PermissionsFor(node, Role.Anonymous).ShouldBe(PermissionType.None);
        }
    }

    [Fact]
    public void PermissionsFor_WithAnUnlistedRole_ShouldGrantNothing() =>
        PlantAuthorization.PermissionsFor(PlantNode.Setpoint, Role.Engineer).ShouldBe(PermissionType.None);

    [Fact]
    public void PermissionsFor_WithAnyGrantedRole_ShouldGrantReceiveEventsOnPlantOnly()
    {
        foreach (var role in new[] { Role.Observer, Role.Operator })
        {
            PlantAuthorization
                .PermissionsFor(PlantNode.Plant, role)
                .HasFlag(PermissionType.ReceiveEvents)
                .ShouldBeTrue();
            PlantAuthorization
                .PermissionsFor(PlantNode.Setpoint, role)
                .HasFlag(PermissionType.ReceiveEvents)
                .ShouldBeFalse();
        }
    }

    [Fact]
    public void RolePermissionsFor_WithAnyNode_ShouldNotIncludeAnonymousRole()
    {
        var anonymousRoleId = ExpandedNodeId.ToNodeId(Role.Anonymous.RoleId, null!);

        foreach (var node in Enum.GetValues<PlantNode>())
        {
            PlantAuthorization
                .RolePermissionsFor(node)
                .ShouldNotContain(permission => permission.RoleId == anonymousRoleId);
        }
    }

    [Fact]
    public void RolePermissionsFor_WithAnyNode_ShouldNeverReturnAnEmptyList()
    {
        foreach (var node in Enum.GetValues<PlantNode>())
        {
            PlantAuthorization.RolePermissionsFor(node).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void RolePermissionsFor_WithSetpoint_ShouldGrantWriteToOperatorOnly()
    {
        var operatorRoleId = ExpandedNodeId.ToNodeId(Role.Operator.RoleId, null!);

        var permissions = PlantAuthorization.RolePermissionsFor(PlantNode.Setpoint);

        permissions.Length.ShouldBe(2);
        foreach (var permission in permissions)
        {
            var grantsWrite = ((PermissionType)permission.Permissions).HasFlag(PermissionType.Write);
            grantsWrite.ShouldBe(permission.RoleId == operatorRoleId);
        }
    }
}
