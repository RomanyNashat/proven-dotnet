using MeterSync.Infrastructure;

namespace MeterSync.Tests.Infrastructure;

public sealed class HttpBaseAddressTests
{
    [Fact]
    public void From_NoTrailingSlash_KeepsTheLastSegmentWhenResolving()
    {
        var baseAddress = HttpBaseAddress.From("https://meters.example.net/v2");

        new Uri(baseAddress, "meters/X/readings").Should().Be(new Uri("https://meters.example.net/v2/meters/X/readings"));
    }

    [Theory]
    [InlineData("https://sms.example.net/", "https://sms.example.net/")]
    [InlineData("https://meters.example.net/v2/", "https://meters.example.net/v2/")]
    [InlineData("https://meters.example.net/v2", "https://meters.example.net/v2/")]
    public void From_AnyForm_EndsWithExactlyOneSlash(string configured, string expected)
    {
        HttpBaseAddress.From(configured).AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("meters/v2")]
    public void From_MissingOrRelative_Throws(string? configured)
    {
        var act = () => HttpBaseAddress.From(configured);

        act.Should().Throw<InvalidOperationException>();
    }
}
