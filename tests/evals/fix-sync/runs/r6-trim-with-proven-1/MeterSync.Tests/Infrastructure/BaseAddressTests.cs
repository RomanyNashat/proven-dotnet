using MeterSync.Infrastructure;

namespace MeterSync.Tests.Infrastructure;

public sealed class BaseAddressTests
{
    [Theory]
    [InlineData("https://meters.example.net/v2", "https://meters.example.net/v2/")]
    [InlineData("https://meters.example.net/v2/", "https://meters.example.net/v2/")]
    [InlineData("https://sms.example.net/", "https://sms.example.net/")]
    [InlineData("https://sms.example.net", "https://sms.example.net/")]
    public void WithTrailingSlash_AbsoluteAddress_EndsWithSlash(string configured, string expected)
    {
        BaseAddress.WithTrailingSlash(configured).AbsoluteUri.Should().Be(expected);
    }

    [Fact]
    public void WithTrailingSlash_PathWithoutSlash_KeepsLastSegmentWhenCombined()
    {
        var combined = new Uri(BaseAddress.WithTrailingSlash("https://meters.example.net/v2"), "meters/A1/readings");

        combined.AbsoluteUri.Should().Be("https://meters.example.net/v2/meters/A1/readings");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("meters/v2")]
    [InlineData("/meters/v2")]
    [InlineData("file:///etc/passwd")]
    public void WithTrailingSlash_MissingOrRelative_Throws(string? configured)
    {
        var act = () => BaseAddress.WithTrailingSlash(configured);

        act.Should().Throw<InvalidOperationException>();
    }
}
