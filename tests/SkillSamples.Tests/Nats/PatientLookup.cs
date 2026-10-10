using NATS.Client.Core;

namespace SkillSamples.Nats;

public sealed record LookupPatient(int PatientId);
public sealed record PatientSummary(int PatientId, string Name);

public static class PatientLookup
{
    public const string Subject = "patient.lookup";

    // The responder: every request gets one reply.
    public static async Task ServeAsync(INatsClient nats, Func<int, PatientSummary> find, CancellationToken ct)
    {
        await foreach (var request in nats.SubscribeAsync<LookupPatient>(Subject, queueGroup: "patient-lookup", cancellationToken: ct))
        {
            if (request.Data is { } lookup)
                await request.ReplyAsync(find(lookup.PatientId), cancellationToken: ct);
        }
    }

    // The caller: a timeout, because a request with no answer would otherwise wait for the default.
    public static async Task<PatientSummary?> AskAsync(INatsClient nats, int patientId, CancellationToken ct)
    {
        var reply = await nats.RequestAsync<LookupPatient, PatientSummary>(
            Subject, new LookupPatient(patientId), replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) }, cancellationToken: ct);
        return reply.Data;
    }
}
