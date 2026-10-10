using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SkillSamples.Production;
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

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AReceptionistTypes500Minutes_IsToldWhichField_FixesIt_AndTheClinicHearsOnlyAfterTheCommit()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var book = scope.ServiceProvider.GetRequiredService<ICommandHandler<BookSlot, int>>();
        var trail = scope.ServiceProvider.GetRequiredService<Trail>().Steps;

        // Given: a 500-minute slot typed by mistake
        var refused = await Assert.ThrowsAsync<ValidationException>(() => book.HandleAsync(new BookSlot(9, 500), CancellationToken.None));

        // When: she corrects it to 30 and the slot is booked; its event goes out once the save is done
        var slot = await book.HandleAsync(new BookSlot(9, 30), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DomainEventDispatcher>().DispatchAsync([new SlotBooked(9)], CancellationToken.None);

        // Then: the refusal named the field and touched nothing; the booking committed before anyone was told
        Assert.Equal(nameof(BookSlot.Minutes), Assert.Single(refused.Errors).PropertyName);
        Assert.Equal(90, slot);
        Assert.Equal(new[] { "begin", "handler", "commit", "notify 9", "audit 9" }, trail);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ThePipelineRuns_AndValidationMessagesNeedNoCulture()
    {
        ProductionConditions.Require();
        using var sp = Build();
        using var scope = sp.CreateScope();
        var book = scope.ServiceProvider.GetRequiredService<ICommandHandler<BookSlot, int>>();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => book.HandleAsync(new BookSlot(9, 500), CancellationToken.None));
        var slot = await book.HandleAsync(new BookSlot(9, 30), CancellationToken.None);

        // FluentValidation falls back to its English messages under the invariant culture; nothing throws.
        Assert.Contains("between 5 and 120", Assert.Single(refused.Errors).ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(90, slot);
    }
}
