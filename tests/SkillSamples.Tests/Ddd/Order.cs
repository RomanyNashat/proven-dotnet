namespace SkillSamples.Ddd;

public sealed class OrderLine
{
    private OrderLine()
    {
    }

    internal OrderLine(int productId, string productName, int quantity, Money unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; } = Money.Zero("SAR");
    public Money Subtotal => UnitPrice.Times(Quantity);

    internal void Increase(int quantity) => Quantity += quantity;
}

/// <summary>
/// The aggregate owns its lines: they change only through its methods, and Total is derived from them.
/// That only holds when the whole aggregate is loaded (see OrderRepository).
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public int Id { get; private set; }
    public int CustomerId { get; private set; }
    public bool IsCancelled { get; private set; }
    public Money Total { get; private set; } = Money.Zero("SAR");
    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(int customerId, string currency) =>
        new() { CustomerId = customerId, Total = Money.Zero(currency) };

    public void AddLine(int productId, string productName, int quantity, Money unitPrice)
    {
        if (IsCancelled)
        {
            throw new InvalidOperationException("A cancelled order can't change");
        }

        var existing = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is null)
        {
            _lines.Add(new OrderLine(productId, productName, quantity, unitPrice));
        }
        else
        {
            existing.Increase(quantity);
        }

        Total = _lines.Aggregate(Money.Zero(Total.Currency), (sum, line) => sum.Add(line.Subtotal));
    }

    public void Cancel() => IsCancelled = true;
}
