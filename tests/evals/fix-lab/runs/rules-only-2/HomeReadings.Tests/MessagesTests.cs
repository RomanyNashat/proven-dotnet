using FluentAssertions;
using HomeReadings.Application;
using Xunit;

namespace HomeReadings.Tests;

public sealed class MessagesTests
{
    [Theory]
    [InlineData("ar", "تم حفظ القراءة")]
    [InlineData("AR", "تم حفظ القراءة")]
    [InlineData("ar-SA", "تم حفظ القراءة")]
    [InlineData("en", "Reading saved")]
    [InlineData(null, "Reading saved")]
    public void Saved_ByLanguageHeader_PicksText(string? language, string expected) =>
        Messages.Saved(language).Should().Be(expected);
}
