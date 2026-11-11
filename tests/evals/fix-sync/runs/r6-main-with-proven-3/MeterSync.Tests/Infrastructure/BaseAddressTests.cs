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
        BaseAddress.WithTrailingSlash(configured).Should().Be(new Uri(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("meters/v2")]
    public void WithTrailingSlash_MissingOrRelative_Throws(string? configured)
    {
        var act = () => BaseAddress.WithTrailingSlash(configured);

        act.Should().Throw<InvalidOperationException>();
    }
}
