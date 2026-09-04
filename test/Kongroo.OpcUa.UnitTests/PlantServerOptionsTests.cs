using System.ComponentModel.DataAnnotations;
using Kongroo.OpcUa.Server;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

/// <summary>
/// Covers the binding defaults and the <c>[Range]</c> contract that decides
/// whether the host boots or refuses to.
/// </summary>
public sealed class PlantServerOptionsTests
{
    [Fact]
    public void Port_WhenNotConfigured_ShouldDefaultToTheStackPort() => new PlantServerOptions().Port.ShouldBe(62552);

    [Theory]
    [InlineData(PlantServerOptions.MinimumPort)]
    [InlineData(PlantServerOptions.DefaultPort)]
    [InlineData(PlantServerOptions.MaximumPort)]
    public void Validate_WithBindablePort_ShouldReportNoErrors(int port) => ValidationErrorsFor(port).ShouldBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PlantServerOptions.MaximumPort + 1)]
    public void Validate_WithUnbindablePort_ShouldReportAnError(int port) =>
        ValidationErrorsFor(port).ShouldNotBeEmpty();

    [Fact]
    public void Validate_WithUnbindablePort_ShouldNameThePortInTheMessage()
    {
        var errors = ValidationErrorsFor(0);

        errors.ShouldContain(error => error.Contains(nameof(PlantServerOptions.Port), StringComparison.Ordinal));
    }

    [Fact]
    public void Users_WhenBoundFromConfiguration_ShouldPopulateEveryEntryWithItsRole()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OpcUa:Users:0:Name"] = "observer",
                    ["OpcUa:Users:0:Password"] = "observer-password",
                    ["OpcUa:Users:0:Role"] = "Observer",
                    ["OpcUa:Users:1:Name"] = "operator",
                    ["OpcUa:Users:1:Password"] = "operator-password",
                    ["OpcUa:Users:1:Role"] = "Operator",
                }
            )
            .Build();

        var options = configuration.GetSection(PlantServerOptions.SectionName).Get<PlantServerOptions>();

        options.ShouldNotBeNull();
        options.Users.Count.ShouldBe(2);
        options.Users[0].Name.ShouldBe("observer");
        options.Users[0].Role.ShouldBe(PlantRole.Observer);
        options.Users[1].Role.ShouldBe(PlantRole.Operator);
    }

    [Fact]
    public void Users_WhenConfigurationHasNoUsersSection_ShouldBeEmpty()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpcUa:Port"] = "62552" })
            .Build();

        var options = configuration.GetSection(PlantServerOptions.SectionName).Get<PlantServerOptions>();

        options.ShouldNotBeNull();
        options.Users.ShouldBeEmpty();
    }

    // Mirrors what ValidateDataAnnotations does at host start, so the [Range]
    // attribute is covered rather than assumed.
    private static List<string> ValidationErrorsFor(int port)
    {
        var options = new PlantServerOptions { Port = port };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results.ConvertAll(result => result.ErrorMessage ?? string.Empty);
    }
}
