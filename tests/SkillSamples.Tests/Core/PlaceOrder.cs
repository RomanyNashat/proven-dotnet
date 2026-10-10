namespace SkillSamples.Core;

public sealed record OrderLine(int ProductId, int Quantity);

// `required` is checked by the compiler and by System.Text.Json: a request body without "lines" fails to
// deserialize (a 400) instead of reaching the handler with a null list.
public sealed record PlaceOrder
{
    public required int CustomerId { get; init; }
    public required IReadOnlyList<OrderLine> Lines { get; init; }
    public string? Notes { get; init; }
}
