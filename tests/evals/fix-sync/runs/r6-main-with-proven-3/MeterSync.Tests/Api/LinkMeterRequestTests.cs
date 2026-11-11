using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class LinkMeterRequestTests
{
    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        new LinkMeterRequest("1234567890", "AB-12_x").Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345abcde")]
    [InlineData("1234567890\n")]
    [InlineData("١٢٣٤٥٦٧٨٩٠")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_BadNationalId_ReturnsNationalIdError(string? nationalId)
    {
        new LinkMeterRequest(nationalId!, "AB-12").Validate().Should().ContainKey(nameof(LinkMeterRequest.NationalId));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../admin")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("AB12\n")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_BadSerialNumber_ReturnsSerialNumberError(string? serial)
    {
        new LinkMeterRequest("1234567890", serial!).Validate().Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }

    [Fact]
    public void Validate_SerialOfFortyOneChars_ReturnsSerialNumberError()
    {
        new LinkMeterRequest("1234567890", new string('A', 41)).Validate().Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
        new LinkMeterRequest("1234567890", new string('A', 40)).Validate().Should().BeEmpty();
    }
}
