using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Localization;

public sealed class PluralTests
{
    private static InMemorySource Texts() => new(new()
    {
        ["en"] = new()
        {
            ["Sms.Upcoming.one"] = "You have 1 upcoming appointment.",
            ["Sms.Upcoming.other"] = "You have {0} upcoming appointments.",
        },
        ["ar"] = new()
        {
            ["Sms.Upcoming.zero"] = "لا توجد لديك مواعيد قادمة.",
            ["Sms.Upcoming.one"] = "لديك موعد واحد قادم.",
            ["Sms.Upcoming.two"] = "لديك موعدان قادمان.",
            ["Sms.Upcoming.few"] = "لديك {0} مواعيد قادمة.",
            ["Sms.Upcoming.many"] = "لديك {0} موعدًا قادمًا.",
            ["Sms.Upcoming.other"] = "لديك {0} موعد قادم.",
        },
    });

    [Theory]
    [InlineData(0, PluralCategory.Zero)]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Two)]
    [InlineData(3, PluralCategory.Few)]
    [InlineData(10, PluralCategory.Few)]
    [InlineData(11, PluralCategory.Many)]
    [InlineData(99, PluralCategory.Many)]
    [InlineData(100, PluralCategory.Other)]
    [InlineData(102, PluralCategory.Other)]
    [InlineData(103, PluralCategory.Few)]
    [InlineData(111, PluralCategory.Many)]
    [InlineData(1000, PluralCategory.Other)]
    public void For_Arabic_FollowsCldr(long count, PluralCategory expected) => Assert.Equal(expected, Plurals.For("ar", count));

    [Theory]
    [InlineData(0, PluralCategory.Other)]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Other)]
    [InlineData(11, PluralCategory.Other)]
    public void For_English_IsOneOrOther(long count, PluralCategory expected) => Assert.Equal(expected, Plurals.For("en", count));

    [Fact]
    public async Task GetCount_ArabicWithoutTheCategory_FallsBackToOther()
    {
        var catalog = new TranslationCatalog(new InMemorySource(new()
        {
            ["en"] = new() { ["Sms.Upcoming.other"] = "You have {0} upcoming appointments." },
            ["ar"] = new() { ["Sms.Upcoming.other"] = "عدد مواعيدك القادمة: {0}." },
        }));

        Assert.Equal("عدد مواعيدك القادمة: 5.", await catalog.GetCountAsync("Sms.Upcoming", "ar", 5));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheReminderCountsAppointments_InTheFormArabicNeeds()
    {
        // Given: a patient whose phone is in Arabic, and the reminder job counting their upcoming visits.
        // The old template was "لديك {0} مواعيد", which is right only from 3 to 10.
        var catalog = new TranslationCatalog(Texts());

        // When: the job writes the line for each count it meets
        var counts = new long[] { 0, 1, 2, 5, 11, 100 };
        var lines = new List<string>();
        foreach (var count in counts) lines.Add(await catalog.GetCountAsync("Sms.Upcoming", LanguageHeader.Parse("ar-SA"), count));

        // Then: each count gets its own form, and an English phone still gets one and other
        Assert.Equal(
        [
            "لا توجد لديك مواعيد قادمة.",
            "لديك موعد واحد قادم.",
            "لديك موعدان قادمان.",
            "لديك 5 مواعيد قادمة.",
            "لديك 11 موعدًا قادمًا.",
            "لديك 100 موعد قادم.",
        ], lines);
        Assert.Equal("You have 1 upcoming appointment.", await catalog.GetCountAsync("Sms.Upcoming", "en", 1));
        Assert.Equal("You have 0 upcoming appointments.", await catalog.GetCountAsync("Sms.Upcoming", "en", 0));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_PluralFormsNeedNoCulture()
    {
        ProductionConditions.Require();
        var catalog = new TranslationCatalog(Texts());

        Assert.Equal("لديك 11 موعدًا قادمًا.", await catalog.GetCountAsync("Sms.Upcoming", "ar", 11));
        Assert.Equal("لديك 1000 موعد قادم.", await catalog.GetCountAsync("Sms.Upcoming", "ar", 1000));
    }
}
