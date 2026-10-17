using ClinicReminders.Tests.Support;
using Microsoft.Extensions.Options;

namespace ClinicReminders.Tests.Api;

[Collection(nameof(HostCollection))]
public sealed class StartupTests
{
    [Fact]
    public void Start_SmsBaseAddressWithoutTrailingSlash_StopsTheDeploy()
    {
        using var factory = new ReminderApiFactory();
        factory.Settings["Sms:BaseAddress"] = "https://sms.example.net/api/v2";

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*must end with '/'*");
    }

    [Fact]
    public void Start_SmsBaseAddressMissing_StopsTheDeploy()
    {
        using var factory = new ReminderApiFactory();
        factory.Settings["Sms:BaseAddress"] = "";

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*BaseAddress*");
    }
}
