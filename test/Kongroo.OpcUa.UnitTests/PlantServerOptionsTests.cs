using System.ComponentModel.DataAnnotations;
using Kongroo.OpcUa.Server;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

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
