using MeterSync.Infrastructure;

namespace MeterSync.Tests.Infrastructure;

public sealed class BaseAddressTests
{
    [Theory]
    [InlineData("https://meters.example.net/v2", "https://meters.example.net/v2/")]
    [InlineData("https://meters.example.net/v2/", "https://meters.example.net/v2/")]
    [InlineData("https://sms.example.net", "https://sms.example.net/")]
    public void WithTrailingSlash_AbsoluteUrl_EndsWithSlash(string configured, string expected)
    {
        var uri = BaseAddress.WithTrailingSlash(configured, "Some:BaseAddress");

        uri.AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void WithTrailingSlash_VendorBaseWithoutSlash_KeepsVersionSegmentForRelativePaths()
    {
        var uri = BaseAddress.WithTrailingSlash("https://meters.example.net/v2", "MeterVendor:BaseAddress");

        new Uri(uri, "meters/ABC/readings").AbsoluteUri
            .Should().Be("https://meters.example.net/v2/meters/ABC/readings");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("meters/v2")]
    public void WithTrailingSlash_MissingOrRelative_ThrowsNamingTheSetting(string? configured)
    {
        var act = () => BaseAddress.WithTrailingSlash(configured, "MeterVendor:BaseAddress");

        act.Should().Throw<InvalidOperationException>().WithMessage("*MeterVendor:BaseAddress*");
    }
}
