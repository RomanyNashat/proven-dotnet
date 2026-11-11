using FluentAssertions;
using MeterSync.Domain;
using Xunit;

namespace MeterSync.Tests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+966501234567")]
    [InlineData("966501234567")]
    [InlineData("00966501234567")]
    [InlineData("0501234567")]
    [InlineData("501234567")]
    [InlineData("050 123 4567")]
    [InlineData("+966-50-123-4567")]
    public void TryToE164_SaudiMobileInAnyCommonForm_ReturnsE164(string input)
    {
        PhoneNumber.TryToE164(input, out var e164).Should().BeTrue();
        e164.Should().Be("+966501234567");
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("call me")]
    public void TryToE164_NotAPhoneNumber_ReturnsFalse(string input) =>
        PhoneNumber.TryToE164(input, out _).Should().BeFalse();
}
