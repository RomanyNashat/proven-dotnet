using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    [Fact]
    public void TryGetId_NameIdentifierClaim_ReturnsId()
    {
        CurrentPatient.TryGetId(User((ClaimTypes.NameIdentifier, "42")), out var id).Should().BeTrue();
        id.Should().Be(42);
    }

    [Fact]
    public void TryGetId_UnmappedSubClaim_ReturnsId()
    {
        CurrentPatient.TryGetId(User(("sub", "7")), out var id).Should().BeTrue();
        id.Should().Be(7);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData(" 5")]
    [InlineData("1.0")]
    [InlineData("99999999999")]
    [InlineData("")]
    public void TryGetId_UnusableSub_ReturnsFalse(string sub)
    {
        CurrentPatient.TryGetId(User(("sub", sub)), out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetId_NoSubject_ReturnsFalse()
    {
        CurrentPatient.TryGetId(User(), out _).Should().BeFalse();
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer"));
}
