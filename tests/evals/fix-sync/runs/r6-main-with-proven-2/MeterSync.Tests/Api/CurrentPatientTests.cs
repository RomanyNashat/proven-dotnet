using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    private static ClaimsPrincipal With(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void TryGetId_SubClaim_ReturnsId()
    {
        CurrentPatient.TryGetId(With(new Claim("sub", "42")), out var id).Should().BeTrue();
        id.Should().Be(42);
    }

    [Fact]
    public void TryGetId_NameIdentifierClaim_ReturnsId()
    {
        CurrentPatient.TryGetId(With(new Claim(ClaimTypes.NameIdentifier, "17")), out var id).Should().BeTrue();
        id.Should().Be(17);
    }

    [Fact]
    public void TryGetId_NoSubject_ReturnsFalse()
    {
        CurrentPatient.TryGetId(With(new Claim("name", "x")), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-3")]
    [InlineData("0")]
    [InlineData("99999999999")]
    [InlineData("1.5")]
    public void TryGetId_NonPositiveOrNonIntegerSubject_ReturnsFalse(string sub)
    {
        CurrentPatient.TryGetId(With(new Claim("sub", sub)), out _).Should().BeFalse();
    }
}
