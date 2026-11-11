using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class LinkMeterRequestValidatorTests
{
    [Theory]
    [InlineData("1234567890", "ABC-123")]
    [InlineData("1", "a")]
    [InlineData("1", "X1.2_3-4")]
    public void Validate_ValidRequest_ReturnsNoErrors(string nationalId, string serial)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest(nationalId, serial)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../admin")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-abc")]
    [InlineData(".hidden")]
    [InlineData("a%2F")]
    [InlineData("ABC\n")]
    public void Validate_BadSerialNumber_ReturnsSerialNumberError(string serial)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest("1234567890", serial))
            .Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }

    [Fact]
    public void Validate_SerialNumberOver40Chars_ReturnsSerialNumberError()
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest("1", new string('a', 41)))
            .Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901")]
    public void Validate_BadNationalId_ReturnsNationalIdError(string nationalId)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest(nationalId, "ABC"))
            .Should().ContainKey(nameof(LinkMeterRequest.NationalId));
    }

    [Fact]
    public void Validate_NullFields_ReturnsBothErrors()
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest(null!, null!))
            .Should().ContainKeys(nameof(LinkMeterRequest.NationalId), nameof(LinkMeterRequest.SerialNumber));
    }
}
