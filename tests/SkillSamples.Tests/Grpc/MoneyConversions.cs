using SkillSamples.GrpcSamples.V1;

namespace SkillSamples.GrpcSamples;

public static class MoneyConversions
{
    private const decimal NanosPerUnit = 1_000_000_000m;

    public static Money ToMoney(this decimal amount, string currency)
    {
        var units = decimal.Truncate(amount);   // toward zero: -12.5 is -12 units and -500,000,000 nanos
        return new Money { Units = (long)units, Nanos = (int)((amount - units) * NanosPerUnit), Currency = currency };
    }

    public static decimal ToDecimal(this Money money) => money.Units + (money.Nanos / NanosPerUnit);
}
