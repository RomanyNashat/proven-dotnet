using HomeReadings.Application;

namespace HomeReadings.Tests.Application;

public sealed class MessagesTests
{
    [Theory]
    [InlineData("ar", "تم حفظ القراءة")]
    [InlineData("AR-sa", "تم حفظ القراءة")]
    [InlineData("en", "Reading saved")]
    [InlineData(null, "Reading saved")]
    [InlineData("fr", "Reading saved")]
    public void Saved_LanguageHeader_PicksTheText(string? header, string expected)
    {
        Messages.Saved(LanguageHeader.Parse(header)).Should().Be(expected);
    }
}
