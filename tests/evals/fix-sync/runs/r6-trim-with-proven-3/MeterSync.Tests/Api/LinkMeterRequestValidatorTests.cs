using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class LinkMeterRequestValidatorTests
{
    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest("1012345678", "SN-1"));

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("12345678901")]
    public void Validate_BadNationalId_ReportsNationalId(string? nationalId)
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest(nationalId!, "SN-1"));

        errors.Should().ContainKey(nameof(LinkMeterRequest.NationalId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingSerial_ReportsSerialNumber(string? serial)
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest("1012345678", serial!));

        errors.Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }

    [Fact]
    public void Validate_SerialOf40AfterTrimming_IsValid()
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest("1012345678", "  " + new string('S', 40) + " "));

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SerialOver40_ReportsSerialNumber()
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest("1012345678", new string('S', 41)));

        errors.Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }
}
