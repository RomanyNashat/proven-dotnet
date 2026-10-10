namespace SkillSamples.Core;

// C# 14 `field`: validation in the setter without declaring a backing field.
public sealed class Product
{
    public required string Name
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A product needs a name.", nameof(value))
            : value.Trim();
    }

    public decimal Price
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A price can't be negative.");
    }
}
