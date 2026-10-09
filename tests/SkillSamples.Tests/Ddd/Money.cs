namespace SkillSamples.Ddd;

/// <summary>A value object: equal by value, immutable, and it refuses to add two currencies.</summary>
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other) =>
        other.Currency == Currency
            ? this with { Amount = Amount + other.Amount }
            : throw new InvalidOperationException($"Can't add {other.Currency} to {Currency}");

    public Money Times(int quantity) => this with { Amount = Amount * quantity };
}
