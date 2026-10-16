using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Xunit;

namespace HiddenTests;

/// <summary>One test per planted finding. Each fails on the original and passes once that finding is fixed.</summary>
public sealed class FindingTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly DateTimeOffset Appointment = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);   // 09:30 in Riyadh

    [Fact]
    public void H01_ScopesValidated_TheServiceStillBuilds()
    {
        using var clinic = new Clinic(validateScopes: true);
        var error = Record.Exception(() => clinic.Services);
        Assert.Null(error);
    }

    [Fact]
    public void H02_TheImage_HasIcuForSqlClient()
    {
        var dockerfile = File.ReadAllLines(Path.Combine(Fixture.Dir, "Dockerfile"));
        var final = dockerfile.Skip(Array.FindLastIndex(dockerfile, l => l.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))).ToArray();
        var text = string.Join('\n', final);

        Assert.DoesNotMatch(@"(?i)DOTNET_SYSTEM_GLOBALIZATION_INVARIANT\s*[=\s]\s*[""']?(true|1)\b", text);
        var image = final[0];
        if (image.Contains("alpine", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Matches(@"(?i)apk\s+add[^\n]*\bicu-libs\b", text);
        }

        Assert.False(image.Contains("chiseled", StringComparison.OrdinalIgnoreCase) && !image.Contains("extra", StringComparison.OrdinalIgnoreCase),
            "a chiseled image without -extra has no ICU");
    }

    [Fact]
    public async Task H04_TheSmsDate_IsGregorianWhateverTheMachinesIcu()
    {
        Assert.False(new CultureInfo("ar-SA").DateTimeFormat.Calendar is GregorianCalendar,
            "conditions: this run needs an ICU whose ar-SA default calendar is Hijri");
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment)).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();

        var text = Text((await clinic.SmsAsync(1)).Single().Body);
        Assert.True(text.Contains("2026", StringComparison.Ordinal) || text.Contains("٢٠٢٦", StringComparison.Ordinal), text);
        Assert.DoesNotContain("1448", text, StringComparison.Ordinal);
        Assert.DoesNotContain("١٤٤٨", text, StringComparison.Ordinal);
    }

    [Fact]
    public void H05_TheMorningTrigger_FiresAtNineInRiyadh()
    {
        using var clinic = new Clinic();
        var cron = clinic.Services.GetRequiredService<IOptions<QuartzOptions>>().Value.Triggers.OfType<ICronTrigger>().Single();
        var next = cron.GetFireTimeAfter(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void H06_ARunMissedDuringADeploy_IsNotDropped()
    {
        using var clinic = new Clinic();
        var options = clinic.Services.GetRequiredService<IOptions<QuartzOptions>>().Value;
        var persistent = (options["quartz.jobStore.type"] ?? "").Contains("JobStoreTX", StringComparison.Ordinal);
        Assert.True(!persistent || options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger,
            "a persistent store re-registers the trigger at start and drops the missed run");
    }

    [Fact]
    public async Task H07_TheSms_GoesToTheProvidersMessagesEndpoint()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment)).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();

        Assert.Equal(new Uri("https://sms.example.net/api/v2/messages"), (await clinic.SmsAsync(1)).Single().Uri);
    }

    [Fact]
    public async Task H08_TheSentLine_IsLoggedWithTheMobileRedacted()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        var created = await Clinic.ScheduleAsync(client, 1, Appointment);
        created.EnsureSuccessStatusCode();
        var id = created.Headers.Location!.ToString().Split('/')[^1];

        await clinic.RunJobAsync();
        await clinic.SmsAsync(1);

        var logs = clinic.Logs();
        AssertNoMobile(logs);
        var sent = logs.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"(?i)\bsent\b") && l.Contains(id, StringComparison.Ordinal)
            && !l.Contains("Executed DbCommand", StringComparison.Ordinal)).ToList();
        Assert.True(sent.Count > 0, "no line says the reminder was sent");
        sent.ForEach(output.WriteLine);
        // Serilog's sink is the way to the log store: the line has to arrive there, not only at a provider.
        Assert.True(sent.Any(l => l.StartsWith('{')), "the sent line never reached Serilog");
        // A template that still names {Mobile} must carry the field, redacted (a string, empty when erased).
        // Serilog as the logger factory never runs the redactor: the classified field comes out null.
        // Leaving the number out of the line is a fix too.
        Assert.All(sent, line => Assert.True(!line.Contains("{Mobile}", StringComparison.Ordinal)
            || line.Contains("\"Mobile\":\"", StringComparison.Ordinal) || line.Contains("; Mobile=", StringComparison.Ordinal)
            || line.Contains("| Mobile=", StringComparison.Ordinal), "the Mobile field vanished: " + line));
    }

    [Fact]
    public async Task H09_PagingThroughABatchImport_ReturnsEveryReminder()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        for (var i = 0; i < 25; i++)
        {
            (await Clinic.ScheduleAsync(client, 1, Appointment.AddDays(5 + i))).EnsureSuccessStatusCode();   // all at the same instant
        }

        var seen = new HashSet<int>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var url = cursor is null ? "/reminders" : "/reminders?cursor=" + Uri.EscapeDataString(cursor);
            using var json = JsonDocument.Parse(await client.GetStringAsync(url));
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
            foreach (var item in items)
            {
                seen.Add(item.GetProperty("id").GetInt32());
            }

            var next = json.RootElement.GetProperty("next");
            if (items.Count == 0 || next.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            cursor = next.GetString();
        }

        Assert.Equal(25, seen.Count);
    }

    [Fact]
    public void H10_Startup_DoesNotChangeTheSchema()
    {
        using var clinic = new Clinic();
        using var client = clinic.CreateClient();
        Assert.Equal(0, Sql.Scalar(clinic.ConnectionString, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES"));
    }

    [Fact]
    public async Task H12_AProviderErrorThatEchoesTheNumber_IsLoggedWithoutIt()
    {
        using var clinic = new Clinic();
        clinic.Sms.Fail = _ => new HttpRequestException($"Recipient {Clinic.Mobile} is not valid");
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment)).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();
        await clinic.SmsAsync(1);

        var logs = clinic.Logs();
        AssertNoMobile(logs);
        Assert.True(logs.Any(l => l.Contains("\"@l\":\"Error\"", StringComparison.Ordinal) || l.Contains("\"@l\":\"Warning\"", StringComparison.Ordinal)
            || l.StartsWith("[Error]", StringComparison.Ordinal) || l.StartsWith("[Warning]", StringComparison.Ordinal)),
            "the failure must still be logged at warning or error");
    }

    [Fact]
    public async Task H13_ACrashAfterTheSend_DoesNotSendItAgain()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment)).EnsureSuccessStatusCode();

        clinic.Crash.Armed = true;
        await clinic.RunJobAsync();
        await clinic.SmsAsync(1);
        clinic.Crash.Armed = false;
        await clinic.RunJobAsync();

        Assert.Single(await clinic.SmsAsync(2, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task H14_APatient_CannotScheduleForAnotherPatient()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync(patient: 1);

        await Clinic.ScheduleAsync(client, 2, Appointment);

        Assert.Equal(0, Sql.Scalar(clinic.ConnectionString, "SELECT COUNT(*) FROM Reminders WHERE PatientId = 2"));
    }

    private static void AssertNoMobile(IReadOnlyList<string> logs)
    {
        var leak = logs.FirstOrDefault(l => l.Contains(Clinic.Mobile, StringComparison.Ordinal));
        Assert.True(leak is null, "the mobile number is in the logs: " + (leak?.Length > 400 ? leak[..400] : leak));
    }

    internal static string Text(string body) => JsonDocument.Parse(body).RootElement.EnumerateObject()
        .Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()).Aggregate("", (a, b) => a + " " + b);
}

/// <summary>Run with no time-zone database (TZDIR pointing at an empty folder), as on a slim image.</summary>
[Trait("Kind", "NoTzdata")]
public sealed class NoTzdataTests
{
    [Fact]
    public async Task H03_WithoutTzdata_TheSmsStillHasRiyadhTime()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, new DateTimeOffset(2026, 11, 3, 6, 30, 0, TimeSpan.Zero))).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();

        var sent = await clinic.SmsAsync(1);
        Assert.Contains("09:30", FindingTests.Text(Assert.Single(sent).Body), StringComparison.Ordinal);
    }
}

internal static class Fixture
{
    public static string Dir => Environment.GetEnvironmentVariable("FIXTURE_DIR")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ClinicReminders"));
}
