using MeterSync.Api;

namespace MeterSync.Tests.Api;

public sealed class LinkMeterRequestValidatorTests
{
    [Theory]
    [InlineData("1012345678", "A1")]
    [InlineData("1", "a")]
    [InlineData("1012345678", "AB-12_x.9")]
    [InlineData("1012345678", "A123456789012345678901234567890123456789")]
    public void Validate_ValidRequest_HasNoErrors(string nationalId, string serialNumber)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest(nationalId, serialNumber)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingFields_ReportsBoth()
    {
        var errors = LinkMeterRequestValidator.Validate(new LinkMeterRequest(null!, null!));

        errors.Keys.Should().BeEquivalentTo(nameof(LinkMeterRequest.NationalId), nameof(LinkMeterRequest.SerialNumber));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("A1/readings")]
    [InlineData("A1%2F")]
    [InlineData("A 1")]
    [InlineData(".hidden")]
    [InlineData("A1\n")]
    public void Validate_SerialThatCouldEscapeVendorPath_IsRejected(string serialNumber)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest("1012345678", serialNumber))
            .Should().ContainKey(nameof(LinkMeterRequest.SerialNumber));
    }

    [Theory]
    [InlineData("1012345678\n")]
    [InlineData("١٠١٢٣٤٥٦٧٨")]
    public void Validate_NationalIdWithTrailingNewlineOrNonAsciiDigits_IsRejected(string nationalId)
    {
        LinkMeterRequestValidator.Validate(new LinkMeterRequest(nationalId, "A1"))
            .Should().ContainKey(nameof(LinkMeterRequest.NationalId));
    }
}
