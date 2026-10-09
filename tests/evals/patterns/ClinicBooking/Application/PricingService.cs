using ClinicBooking.Domain;

namespace ClinicBooking.Application;

public interface IPriceCalculator
{
    decimal Calculate(decimal basePrice);
}

public sealed class StandardPriceCalculator : IPriceCalculator
{
    public decimal Calculate(decimal basePrice) => basePrice;
}

public sealed class PriceCalculatorFactory
{
    public IPriceCalculator Create() => new StandardPriceCalculator();
}

public sealed class PricingService
{
    private const decimal BaseFee = 150m;

    public decimal FeeFor(Appointment appointment)
    {
        var standard = new PriceCalculatorFactory().Create().Calculate(BaseFee);
        return appointment.Insurance switch
        {
            InsuranceType.SelfPay => standard,
            InsuranceType.Government => 0m,
            InsuranceType.Private => standard * 0.2m,
            _ => throw new ArgumentOutOfRangeException(nameof(appointment)),
        };
    }
}
