using System.Security.Claims;
using ClinicReminders.Api;
using Microsoft.AspNetCore.Http;

namespace ClinicReminders.Tests.Api;

public sealed class HttpCurrentUserTests
{
    [Fact]
    public void TryGetPatientId_NumericSub_ReturnsIt()
    {
        User(new Claim("sub", "42")).TryGetPatientId(out var id).Should().BeTrue();
        id.Should().Be(42);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("99999999999")]
    public void TryGetPatientId_SubNotAPatientId_ReturnsFalse(string sub)
    {
        User(new Claim("sub", sub)).TryGetPatientId(out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetPatientId_NoSub_ReturnsFalse()
    {
        User().TryGetPatientId(out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetPatientId_NoRequest_ReturnsFalse()
    {
        new HttpCurrentUser(new HttpContextAccessor()).TryGetPatientId(out _).Should().BeFalse();
    }

    private static HttpCurrentUser User(params Claim[] claims) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } });
}
