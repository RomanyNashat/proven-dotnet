using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SkillSamples.Architecture.Shop.Application;
using SkillSamples.Architecture.Shop.Domain;
using SkillSamples.Architecture.Shop.Domain.ValueObjects;
using SkillSamples.Cqrs;

// A small service laid out in the four layers, for the architecture rules to check. In a real solution
// each layer is its own project; here each is a namespace, and the rules take either.

namespace SkillSamples.Architecture.Shop.Domain.ValueObjects
{
    public sealed record Money(decimal Amount, string Currency);
}

namespace SkillSamples.Architecture.Shop.Domain
{
    public sealed class Order
    {
        private Order() { }

        public int Id { get; private set; }
        public Money Total { get; private set; } = new(0m, "SAR");

        public static Order Place(Money total) => new() { Total = total };
    }

    public interface IOrderRepository
    {
        void Add(Order order);
    }
}

namespace SkillSamples.Architecture.Shop.Application
{
    public sealed record PlaceOrder(decimal Amount) : ICommand<int>;

    public sealed class PlaceOrderHandler(IOrderRepository orders) : ICommandHandler<PlaceOrder, int>
    {
        public Task<int> HandleAsync(PlaceOrder command, CancellationToken ct)
        {
            var order = Order.Place(new Money(command.Amount, "SAR"));
            orders.Add(order);
            return Task.FromResult(order.Id);
        }
    }

    public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
    {
        public PlaceOrderValidator() => RuleFor(c => c.Amount).GreaterThan(0);
    }
}

namespace SkillSamples.Architecture.Shop.Infrastructure
{
    public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
    }

    public sealed class OrderRepository(ShopDbContext db) : IOrderRepository
    {
        public void Add(Order order) => db.Orders.Add(order);
    }
}

namespace SkillSamples.Architecture.Shop.Api
{
    public static class OrderEndpoints
    {
        public static Task<int> PlaceAsync(PlaceOrder command, ICommandHandler<PlaceOrder, int> handler, CancellationToken ct) =>
            handler.HandleAsync(command, ct);
    }
}
