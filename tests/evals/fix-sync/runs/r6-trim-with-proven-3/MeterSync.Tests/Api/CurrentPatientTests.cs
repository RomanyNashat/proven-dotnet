using System.Security.Claims;
using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class CurrentPatientTests
{
    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public void Id_NumericSubject_ReturnsIt(string claimType)
    {
        var user = UserWith(new Claim(claimType, "42"));

        CurrentPatient.Id(user).Should().Be(42);
    }

    [Fact]
    public void Id_NoSubject_ReturnsNull()
    {
        var user = UserWith(new Claim("name", "someone"));

        CurrentPatient.Id(user).Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData(" 42")]
    [InlineData("99999999999")]
    public void Id_SubjectNotAPatientId_ReturnsNull(string sub)
    {
        var user = UserWith(new Claim("sub", sub));

        CurrentPatient.Id(user).Should().BeNull();
    }

    [Fact]
    public void Id_AnonymousPrincipal_ReturnsNull()
    {
        CurrentPatient.Id(new ClaimsPrincipal()).Should().BeNull();
    }

    private static ClaimsPrincipal UserWith(Claim claim) => new(new ClaimsIdentity([claim], "test"));
}
