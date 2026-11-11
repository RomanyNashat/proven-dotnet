using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    private static ClaimsPrincipal With(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void TryGetId_NumericSub_ReturnsId()
    {
        CurrentPatient.TryGetId(With(new Claim("sub", "42"))).Should().Be(42);
    }

    [Fact]
    public void TryGetId_NoSub_ReturnsNull()
    {
        CurrentPatient.TryGetId(With()).Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData(" 42")]
    [InlineData("99999999999")]
    [InlineData("4٠2")]
    public void TryGetId_InvalidSub_ReturnsNull(string sub)
    {
        CurrentPatient.TryGetId(With(new Claim("sub", sub))).Should().BeNull();
    }
}
