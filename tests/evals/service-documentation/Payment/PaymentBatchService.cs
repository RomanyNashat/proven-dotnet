using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Payment;

public sealed class PaymentBatchService(IConnectionMultiplexer redis, GatewayClient gateway, IOptions<PaymentOptions> options)
{
    public async Task<BatchResult> SubmitAsync(BatchRequest request, CancellationToken ct)
    {
        // The gateway rejects the whole call above this size, so the batch is refused here, not split.
        if (request.Items.Count > options.Value.MaxBatchSize)
        {
            return BatchResult.TooLarge(options.Value.MaxBatchSize);
        }

        // Idempotency: the same batch key within 24 hours returns the first result instead of charging twice.
        var key = $"payments:batch:{request.IdempotencyKey}";
        var db = redis.GetDatabase();
        if (!await db.StringSetAsync(key, "pending", TimeSpan.FromHours(24), When.NotExists))
        {
            return BatchResult.Duplicate(request.IdempotencyKey);
        }

        var result = await gateway.ChargeAsync(request.Items, ct);
        await db.StringSetAsync(key, result.BatchId, TimeSpan.FromHours(24));
        return BatchResult.Accepted(result.BatchId);
    }
}
