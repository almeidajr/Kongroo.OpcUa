using Bogus;
using Shouldly;

namespace Kongroo.OpcUa.UnitTests;

// Placeholder smoke test — replace with real tests. Verifies the test toolchain
// (Bogus + Shouldly + Microsoft Testing Platform) is wired up correctly.
public class SmokeTests
{
    [Fact]
    public void RandomInt_WhenGivenARange_ShouldReturnValueWithinThatRange()
    {
        var value = new Faker().Random.Int(1, 100);

        value.ShouldBeInRange(1, 100);
    }
}
