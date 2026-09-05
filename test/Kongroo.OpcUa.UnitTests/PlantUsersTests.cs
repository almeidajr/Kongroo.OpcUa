using System.Text;
using Kongroo.OpcUa.Server;
using Opc.Ua.Server;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

/// <summary>
/// Covers seeding the in-memory user store, including the validation that would otherwise be
/// silently skipped: data annotations on collection items never run.
/// </summary>
public sealed class PlantUsersTests
{
    private static PlantUserOptions ValidUser(string name = "observer", string password = "observer-password") =>
        new()
        {
            Name = name,
            Password = password,
            Role = PlantRole.Observer,
        };

    [Fact]
    public void CreateUserDatabase_WithSeededUser_ShouldAcceptTheSeededPassword()
    {
        var database = PlantUsers.CreateUserDatabase([ValidUser()]);

        database.CheckCredentials("observer", Encoding.UTF8.GetBytes("observer-password")).ShouldBeTrue();
    }

    [Fact]
    public void CreateUserDatabase_WithSeededUser_ShouldRejectAWrongPassword()
    {
        var database = PlantUsers.CreateUserDatabase([ValidUser()]);

        database.CheckCredentials("observer", Encoding.UTF8.GetBytes("not-the-password")).ShouldBeFalse();
    }

    [Fact]
    public void CreateUserDatabase_WithNoUsers_ShouldReturnAnEmptyDatabase()
    {
        var database = PlantUsers.CreateUserDatabase([]);

        database.GetUsers().ShouldBeEmpty();
    }

    [Fact]
    public void CreateUserDatabase_WithDuplicateUserNames_ShouldThrow() =>
        Should.Throw<InvalidOperationException>(() =>
            PlantUsers.CreateUserDatabase([ValidUser(), ValidUser(password: "another-password")])
        );

    [Fact]
    public void CreateUserDatabase_WithBlankUserName_ShouldThrow() =>
        Should.Throw<InvalidOperationException>(() => PlantUsers.CreateUserDatabase([ValidUser(name: "   ")]));

    [Fact]
    public void CreateUserDatabase_WithBlankPassword_ShouldThrow() =>
        Should.Throw<InvalidOperationException>(() => PlantUsers.CreateUserDatabase([ValidUser(password: "")]));

    [Fact]
    public void CreateUserDatabase_WithShortPassword_ShouldThrow() =>
        Should.Throw<InvalidOperationException>(() => PlantUsers.CreateUserDatabase([ValidUser(password: "short")]));

    [Fact]
    public void ToRole_WithObserver_ShouldReturnTheWellKnownObserverRole() =>
        PlantUsers.ToRole(PlantRole.Observer).ShouldBe(Role.Observer);

    [Fact]
    public void ToRole_WithOperator_ShouldReturnTheWellKnownOperatorRole() =>
        PlantUsers.ToRole(PlantRole.Operator).ShouldBe(Role.Operator);

    [Fact]
    public void BrowseNameFor_WithObserver_ShouldReturnTheWellKnownObserverBrowseName() =>
        PlantUsers.BrowseNameFor(PlantRole.Observer).ShouldBe(Opc.Ua.BrowseNames.WellKnownRole_Observer);

    [Fact]
    public void BrowseNameFor_WithOperator_ShouldReturnTheWellKnownOperatorBrowseName() =>
        PlantUsers.BrowseNameFor(PlantRole.Operator).ShouldBe(Opc.Ua.BrowseNames.WellKnownRole_Operator);

    [Fact]
    public void ToString_WithAPasswordSet_ShouldNotRevealThePassword()
    {
        var user = ValidUser(password: "a-secret-password");

        user.ToString().ShouldNotContain("a-secret-password");
    }

    [Fact]
    public void ToString_WithAUserNameAndRole_ShouldReportBoth()
    {
        var rendered = ValidUser().ToString();

        rendered.ShouldContain("observer");
        rendered.ShouldContain(nameof(PlantRole.Observer));
    }
}
