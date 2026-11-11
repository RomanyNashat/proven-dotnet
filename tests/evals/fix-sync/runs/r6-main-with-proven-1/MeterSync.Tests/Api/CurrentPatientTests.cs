using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public void TryGetId_IntegerSubject_ReturnsId(string claimType)
    {
        var user = UserWith(new Claim(claimType, "42"));

        CurrentPatient.TryGetId(user, out var id).Should().BeTrue();
        id.Should().Be(42);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("99999999999")]
    [InlineData("4 2")]
    public void TryGetId_NonPositiveOrNonIntegerSubject_ReturnsFalse(string sub)
    {
        CurrentPatient.TryGetId(UserWith(new Claim("sub", sub)), out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetId_NoSubject_ReturnsFalse()
    {
        CurrentPatient.TryGetId(UserWith(new Claim("name", "x")), out _).Should().BeFalse();
    }

    private static ClaimsPrincipal UserWith(Claim claim) => new(new ClaimsIdentity([claim], "test"));
}
