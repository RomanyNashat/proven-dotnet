using Microsoft.AspNetCore.Http.HttpResults;

namespace Payment;

public static class PaymentEndpoints
{
    public static async Task<Results<Accepted<BatchAccepted>, Conflict<ProblemDetails>, BadRequest<ProblemDetails>>> SubmitBatch(
        BatchRequest request, PaymentBatchService service, CancellationToken ct)
    {
        var result = await service.SubmitAsync(request, ct);
        return result.Kind switch
        {
            BatchResultKind.TooLarge => TypedResults.BadRequest(new ProblemDetails { Title = $"At most {result.Limit} items per batch" }),
            BatchResultKind.Duplicate => TypedResults.Conflict(new ProblemDetails { Title = "This batch was already submitted" }),
            _ => TypedResults.Accepted($"/api/payments/{result.BatchId}", new BatchAccepted(result.BatchId!))
        };
    }

    public static async Task<Results<Ok<PaymentStatus>, NotFound>> GetPayment(string id, GatewayClient gateway, CancellationToken ct)
    {
        var status = await gateway.GetStatusAsync(id, ct);
        return status is null ? TypedResults.NotFound() : TypedResults.Ok(status);
    }
}
