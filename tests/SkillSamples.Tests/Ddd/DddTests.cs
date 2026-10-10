using Microsoft.EntityFrameworkCore;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Ddd;

/// <summary>The same checks on both engines.</summary>
public abstract class DddTests<TFixture>(TFixture pg) where TFixture : OrdersFixture
{
    private async Task<int> PlaceAsync(params (int Product, int Quantity, decimal Price)[] lines)
    {
        await using var db = pg.NewContext();
        var order = Order.Place(customerId: 7, "SAR");
        foreach (var (product, quantity, price) in lines)
        {
            order.AddLine(product, $"product {product}", quantity, new Money(price, "SAR"));
        }

        await new OrderRepository(db).AddAsync(order, CancellationToken.None);
        Assert.True(order.Id > 0);   // the id exists before SaveChanges, so an event can carry it
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task WholeAggregateLoaded_AddLine_TotalCoversEveryLine()
    {
        var id = await PlaceAsync((1, 2, 10m), (2, 1, 5m));   // 25 SAR

        await using (var db = pg.NewContext())
        {
            var order = (await new OrderRepository(db).GetAsync(id, CancellationToken.None))!;
            order.AddLine(3, "product 3", 1, new Money(7m, "SAR"));
            await db.SaveChangesAsync();
        }

        await using var check = pg.NewContext();
        var saved = await check.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
        Assert.Equal(3, saved.Lines.Count);
        Assert.Equal(new Money(32m, "SAR"), saved.Total);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AMedicineIsAddedTwice_ThenTheOrderIsCancelled_ALateAddIsRefused_AndTheTotalHolds()
    {
        // Given: an order with one pack of medicine 5
        var id = await PlaceAsync((5, 1, 12.5m));

        // When: the same medicine is added again from another screen, the order is cancelled, and an add
        // that was already on its way arrives afterwards
        await ChangeAsync(order => order.AddLine(5, "product 5", 2, new Money(12.5m, "SAR")));
        await ChangeAsync(order => order.Cancel());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChangeAsync(order => order.AddLine(6, "product 6", 1, new Money(3m, "SAR"))));

        // Then: one line with the quantities added, the total of three packs, and the order cancelled
        await using var check = pg.NewContext();
        var saved = await check.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
        var line = Assert.Single(saved.Lines);
        Assert.Equal((5, 3), (line.ProductId, line.Quantity));
        Assert.Equal(new Money(37.5m, "SAR"), saved.Total);
        Assert.True(saved.IsCancelled);

        async Task ChangeAsync(Action<Order> change)
        {
            await using var db = pg.NewContext();
            change((await new OrderRepository(db).GetAsync(id, CancellationToken.None))!);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AggregateLoadedWithoutItsLines_AddLine_SavesAWrongTotal()
    {
        var id = await PlaceAsync((1, 2, 10m), (2, 1, 5m));   // 25 SAR

        await using (var db = pg.NewContext())
        {
            var order = (await db.Orders.FindAsync(id))!;      // the common partial load: no lines
            order.AddLine(3, "product 3", 1, new Money(7m, "SAR"));
            await db.SaveChangesAsync();
        }

        await using var check = pg.NewContext();
        var saved = await check.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
        Assert.Equal(3, saved.Lines.Count);                      // all three lines are in the table...
        Assert.Equal(new Money(7m, "SAR"), saved.Total);         // ...but the total only counts the new one
    }

    [Fact]
    public async Task SameProductTwice_OneLineWithTheQuantitiesAdded()
    {
        var id = await PlaceAsync((1, 2, 10m), (1, 3, 10m));

        await using var check = pg.NewContext();
        var saved = await check.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
        Assert.Equal(5, Assert.Single(saved.Lines).Quantity);
        Assert.Equal(new Money(50m, "SAR"), saved.Total);
    }

    [Fact]
    public void Money_DifferentCurrencies_DoNotAdd() =>
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "SAR").Add(new Money(1m, "USD")));
}

public sealed class PostgresDddTests(PostgresOrders pg) : DddTests<PostgresOrders>(pg), IClassFixture<PostgresOrders>;

public sealed class SqlServerDddTests(SqlServerOrders db) : DddTests<SqlServerOrders>(db), IClassFixture<SqlServerOrders>;

/// <summary>The slim image's conditions. PostgreSQL only: SqlClient can't connect without ICU.</summary>
public sealed class PostgresDddProductionTests(PostgresOrders pg) : IClassFixture<PostgresOrders>
{
    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ArabicProductNamesAndExactTotalsRoundTrip()
    {
        ProductionConditions.Require();
        int id;
        await using (var db = pg.NewContext())
        {
            var order = Order.Place(customerId: 9, "SAR");
            order.AddLine(1, "باراسيتامول ٥٠٠ ملغ", 3, new Money(4.35m, "SAR"));
            await new OrderRepository(db).AddAsync(order, CancellationToken.None);
            await db.SaveChangesAsync();
            id = order.Id;
        }

        await using var check = pg.NewContext();
        var saved = await check.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
        Assert.Equal("باراسيتامول ٥٠٠ ملغ", Assert.Single(saved.Lines).ProductName);
        Assert.Equal(new Money(13.05m, "SAR"), saved.Total);
    }
}
