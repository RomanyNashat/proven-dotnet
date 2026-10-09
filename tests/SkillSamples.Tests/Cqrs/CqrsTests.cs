using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SkillSamples.Cqrs;

public sealed record BookSlot(int ClinicId, int Minutes) : ICommand<int>;
public sealed record GetSlotCount(int ClinicId) : IQuery<int>;
public sealed record SlotBooked(int ClinicId) : IDomainEvent;

public sealed class Trail
{
    public List<string> Steps { get; } = [];
}

public sealed class BookSlotHandler(Trail trail) : ICommandHandler<BookSlot, int>
{
    public Task<int> HandleAsync(BookSlot command, CancellationToken ct)
    {
        trail.Steps.Add("handler");
        return Task.FromResult(command.ClinicId * 10);
    }
}

public sealed class GetSlotCountHandler(Trail trail) : IQueryHandler<GetSlotCount, int>
{
    public Task<int> HandleAsync(GetSlotCount query, CancellationToken ct)
    {
        trail.Steps.Add("query");
        return Task.FromResult(3);
    }
}

public sealed class BookSlotValidator : AbstractValidator<BookSlot>
{
    public BookSlotValidator() => RuleFor(c => c.Minutes).InclusiveBetween(5, 120);
}

public sealed class RecordingUnitOfWork(Trail trail) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        trail.Steps.Add("begin");
        var result = await work(ct);
        trail.Steps.Add("commit");
        return result;
    }
}

public sealed class NotifyClinic(Trail trail) : IDomainEventHandler<SlotBooked>
{
    public Task HandleAsync(SlotBooked domainEvent, CancellationToken ct) { trail.Steps.Add($"notify {domainEvent.ClinicId}"); return Task.CompletedTask; }
}

public sealed class AuditSlot(Trail trail) : IDomainEventHandler<SlotBooked>
{
    public Task HandleAsync(SlotBooked domainEvent, CancellationToken ct) { trail.Steps.Add($"audit {domainEvent.ClinicId}"); return Task.CompletedTask; }
}

public sealed class CqrsTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<Trail>();
        services.AddScoped<IUnitOfWork, RecordingUnitOfWork>();
        services.AddValidatorsFromAssemblyContaining<BookSlotValidator>();
        services.AddCqrsHandlers(typeof(BookSlotHandler).Assembly);
        services.AddScoped<IDomainEventHandler<SlotBooked>, NotifyClinic>();
        services.AddScoped<IDomainEventHandler<SlotBooked>, AuditSlot>();
        services.AddScoped<DomainEventDispatcher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Command_RunsInsideTheTransaction_AfterValidation()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BookSlot, int>>();

        var result = await handler.HandleAsync(new BookSlot(7, 30), CancellationToken.None);

        Assert.Equal(70, result);
        Assert.Equal(new[] { "begin", "handler", "commit" }, scope.ServiceProvider.GetRequiredService<Trail>().Steps);
        Assert.IsType<LoggingCommandDecorator<BookSlot, int>>(handler);   // outermost
    }

    [Fact]
    public async Task InvalidCommand_StopsBeforeTheTransaction()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BookSlot, int>>();

        await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(new BookSlot(7, 500), CancellationToken.None));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<Trail>().Steps);
    }

    [Fact]
    public async Task Query_GetsLoggingOnly_NoTransaction()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSlotCount, int>>();

        Assert.Equal(3, await handler.HandleAsync(new GetSlotCount(7), CancellationToken.None));
        Assert.Equal(new[] { "query" }, scope.ServiceProvider.GetRequiredService<Trail>().Steps);
        Assert.IsType<LoggingQueryDecorator<GetSlotCount, int>>(handler);
    }

    [Fact]
    public async Task DomainEvent_ReachesEveryHandler()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DomainEventDispatcher>()
            .DispatchAsync([new SlotBooked(4)], CancellationToken.None);

        Assert.Equal(new[] { "notify 4", "audit 4" }, scope.ServiceProvider.GetRequiredService<Trail>().Steps);
    }
}
