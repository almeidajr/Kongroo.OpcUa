using System.Text;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Kongroo.OpcUa.Server;

/// <summary>
/// Seeds the in-memory user store from configuration.
/// </summary>
/// <remarks>
/// This type owns the validation of <see cref="PlantUserOptions"/>. Data annotations cannot do
/// it: <c>ValidateDataAnnotations</c> checks only an options object's top-level properties and
/// does not recurse into collection items, so attributes on <see cref="PlantUserOptions"/> would
/// look like validation in review and do nothing at runtime.
/// </remarks>
internal static class PlantUsers
{
    /// <summary>Shortest password accepted for a seeded user, in characters.</summary>
    internal const int MinimumPasswordLength = 8;

    /// <summary>
    /// Builds the in-memory user store.
    /// </summary>
    /// <param name="users">
    /// Users to seed. May be empty, which yields an empty store: the server still boots, and
    /// every password login is refused because no account exists.
    /// </param>
    /// <returns>
    /// A store holding one PBKDF2-SHA512 hashed entry per user. The caller owns it and registers
    /// it as a singleton for the process lifetime.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// A user has a blank name, a blank password, a password shorter than
    /// <see cref="MinimumPasswordLength"/>, or a name already used by an earlier entry. Thrown
    /// before the host is built, so a misconfigured user refuses to boot rather than producing a
    /// weak account.
    /// </exception>
    internal static LinqUserDatabase CreateUserDatabase(IEnumerable<PlantUserOptions> users)
    {
        var database = new LinqUserDatabase();

        foreach (var user in users)
        {
            Validate(user);

            // LinqUserDatabase reports a duplicate by returning false rather than throwing, so the
            // result is checked: AddOrUpdate overwrites the existing hash and roles before
            // returning false, so an unchecked result would silently let the last entry win.
            if (!database.CreateUser(user.Name, Encoding.UTF8.GetBytes(user.Password), [ToRole(user.Role)]))
            {
                throw new InvalidOperationException($"Duplicate user name in configuration: '{user.Name}'.");
            }
        }

        return database;
    }

    /// <summary>
    /// Maps a configuration role onto the well-known Part 18 role it stands for.
    /// </summary>
    /// <param name="role">The configured role.</param>
    /// <returns>The matching well-known <see cref="Role"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="role"/> is not a declared <see cref="PlantRole"/>.
    /// </exception>
    internal static Role ToRole(PlantRole role) =>
        role switch
        {
            PlantRole.Observer => Role.Observer,
            PlantRole.Operator => Role.Operator,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown plant role."),
        };

    /// <summary>
    /// The BrowseName of the well-known role <paramref name="role"/> stands for.
    /// </summary>
    /// <param name="role">The configured role.</param>
    /// <returns>
    /// The BrowseName <c>ConfigureRoles</c> matches on. It must equal the name
    /// <see cref="RoleManager"/> seeded, or the stack creates a new role that grants nothing
    /// instead of resolving the well-known one.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="role"/> is not a declared <see cref="PlantRole"/>.
    /// </exception>
    internal static string BrowseNameFor(PlantRole role) =>
        // Fully qualified, not needless verbosity: the model generator emits a
        // Kongroo.OpcUa.Server.BrowseNames class into this same namespace, so a bare BrowseNames
        // binds to the generated class, which has no WellKnownRole_* members. A `using Opc.Ua;`
        // would only make PlantUsersTests.cs ambiguous (CS0104) instead.
        role switch
        {
            PlantRole.Observer => Opc.Ua.BrowseNames.WellKnownRole_Observer,
            PlantRole.Operator => Opc.Ua.BrowseNames.WellKnownRole_Operator,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown plant role."),
        };

    /// <summary>
    /// The single definition of the role-to-user mapping <c>ConfigureRoles</c> needs: one role
    /// definition per <see cref="PlantRole"/>, holding a <see cref="RoleIdentityMappingOptions"/>
    /// for every user that holds it. Both <c>Program.cs</c> and
    /// <c>PlantServerFixture.cs</c> call this so the integration tests cannot drift from what the
    /// server itself configures.
    /// </summary>
    /// <param name="roleOptions">The role configuration being built.</param>
    /// <param name="users">Users to map onto their <see cref="PlantUserOptions.Role"/>.</param>
    internal static void ConfigureRoles(RoleConfigurationOptions roleOptions, IEnumerable<PlantUserOptions> users)
    {
        foreach (var role in Enum.GetValues<PlantRole>())
        {
            var definition = new RoleDefinitionOptions { Name = BrowseNameFor(role) };

            foreach (var user in users.Where(candidate => candidate.Role == role))
            {
                definition.Identities.Add(
                    new RoleIdentityMappingOptions
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = user.Name,
                    }
                );
            }

            roleOptions.Roles.Add(definition);
        }
    }

    private static void Validate(PlantUserOptions user)
    {
        if (string.IsNullOrWhiteSpace(user.Name))
        {
            throw new InvalidOperationException("A configured user has a blank name.");
        }

        if (string.IsNullOrWhiteSpace(user.Password))
        {
            throw new InvalidOperationException($"User '{user.Name}' has a blank password.");
        }

        if (user.Password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"User '{user.Name}' has a password shorter than {MinimumPasswordLength} characters."
            );
        }
    }
}
