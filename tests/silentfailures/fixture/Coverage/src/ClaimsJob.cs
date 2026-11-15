namespace Coverage;

public sealed class ClaimsJob(IClaimStore store, InsurerClient insurer, IAuditLog audit, ILogger<ClaimsJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var claim in await store.PendingAsync(ct))
        {
            try
            {
                await insurer.NotifyAsync(claim.PatientId, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "تعذر إرسال المطالبة {ClaimId}", claim.Id);
            }
            claim.MarkSent();
        }

        foreach (var claim in await store.RejectedAsync(ct))
        {
            try
            {
                await store.ArchiveAsync(claim, ct);
            }
            catch (IOException)
            {
                continue;
            }
        }

        _ = audit.WriteAsync("claims job ran", ct);
        await store.SaveAsync(ct);
    }
}
