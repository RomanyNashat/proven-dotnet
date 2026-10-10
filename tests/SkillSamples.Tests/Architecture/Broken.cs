using Microsoft.EntityFrameworkCore;

// The same layers with the mistakes the rules exist to catch.

namespace SkillSamples.Architecture.Broken.Domain
{
    // A domain service that reaches for the database.
    public sealed class OrderPricing(DbContext db)
    {
        public Task<int> CountAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    }

    public sealed class Invoice
    {
        public decimal Total { get; set; }   // state changed from anywhere
    }
}

namespace SkillSamples.Architecture.Broken.Application
{
    public sealed record Ping : ICommand<int>, ICommand<string>;

    public class PingHandler : ICommandHandler<Ping, int>   // not sealed
    {
        public Task<int> HandleAsync(Ping command, CancellationToken ct) => Task.FromResult(1);
    }

    public sealed class PingProcessor : ICommandHandler<Ping, string>   // a handler without the suffix
    {
        public Task<string> HandleAsync(Ping command, CancellationToken ct) => Task.FromResult("pong");
    }
}

namespace SkillSamples.Architecture.Broken.Api
{
    // An endpoint that skips the application layer.
    public static class InvoiceEndpoints
    {
        public static Task<int> CountAsync(DbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    }
}
