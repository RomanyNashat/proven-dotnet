using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    [Fact]
    public void TryGetId_NameIdentifierClaim_ReturnsId()
    {
        var user = UserWith(new Claim(ClaimTypes.NameIdentifier, "42"));

        CurrentPatient.TryGetId(user, out var id).Should().BeTrue();
        id.Should().Be(42);
    }

    [Fact]
    public void TryGetId_SubClaimOnly_ReturnsId()
    {
        var user = UserWith(new Claim("sub", "7"));

        CurrentPatient.TryGetId(user, out var id).Should().BeTrue();
        id.Should().Be(7);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void TryGetId_NonIntegerSub_ReturnsFalse(string sub) =>
        CurrentPatient.TryGetId(UserWith(new Claim("sub", sub)), out _).Should().BeFalse();

    [Fact]
    public void TryGetId_NoClaim_ReturnsFalse() =>
        CurrentPatient.TryGetId(new ClaimsPrincipal(new ClaimsIdentity()), out _).Should().BeFalse();

    private static ClaimsPrincipal UserWith(Claim claim) => new(new ClaimsIdentity([claim], "test"));
}
