---
name: scaffold
description: "Generate a new microservice, worker, job, or cronjob. Microservices support 3 architectures (--simple, --clean, --cqrs) × 2 API styles (--minimal-api, --controllers) on PostgreSQL or SQL Server. Asks questions if flags are missing. Usage: /scaffold microservice <Name> [--simple|--clean|--cqrs] [--minimal-api|--controllers] [--postgres|--sqlserver]"
allowed-tools: Read, Write, Edit, Bash, Grep, Glob
---

## Scaffold

### Usage
```
/scaffold microservice <Name> [--simple|--clean|--cqrs] [--minimal-api|--controllers] [--postgres|--sqlserver]
/scaffold worker <Name>
/scaffold job <Name>
/scaffold cronjob <Name>
/scaffold endpoint <Name>   (add endpoint group to existing service)
/scaffold from-sql          (generate the data layer FROM existing SQL — schema, stored procs, or a .sql file)
```

### from-sql mode
`/scaffold from-sql` generates a .NET service layer **from existing SQL** — a table schema, stored
procedures, or a SQL script. It asks what SQL you have, then produces Dapper DTOs, repositories,
service classes, and endpoints matched to the SQL (correct types, nullability, parameterized queries).
See `skills/scaffold-from-sql/`. Plan Mode; the developer approves before anything is written.

### Flag behavior
- **All three flags provided** → generate immediately, no questions
- **Some missing** → ask only the missing questions:
  1. Architecture? → simple / clean / cqrs
  2. API style? → minimal-api / controllers
  3. Database? → postgres / sqlserver (if the repo already has one, say which and confirm)

### Database engine
The templates below show PostgreSQL. With `--sqlserver`, generate the SQL Server column of this table
instead; everything else is the same.

| | `--postgres` | `--sqlserver` |
|---|---|---|
| EF provider package | `Npgsql.EntityFrameworkCore.PostgreSQL` | `Microsoft.EntityFrameworkCore.SqlServer` |
| Registration | `o.UseNpgsql(connectionString)` | `o.UseSqlServer(connectionString)` |
| Keys | `UseIdentityAlwaysColumn()` | `UseIdentityColumn()` |
| Strings | `HasMaxLength(n)` | `HasMaxLength(n).IsUnicode(...)`; Arabic text `nvarchar` |
| Timestamps (UTC) | `timestamptz` | `datetime2(3)` + a UTC converter for `DateTimeOffset` |
| Concurrency token | `xmin` shadow property (`uint`) | `rowversion` shadow property (`byte[]`) |
| Health check | `AddNpgSql(...)` (`AspNetCore.HealthChecks.NpgSql`) | `AddSqlServer(...)` (`AspNetCore.HealthChecks.SqlServer`) |
| Test container | `PostgreSqlBuilder("postgres:17")`, Respawn `DbAdapter.Postgres` | `MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")`, Respawn `DbAdapter.SqlServer` |

The column rules and the tested mapping are in `efcore-patterns` §2 and §8; the test factory is in
`testing-integration` §1. The connection string key is `ConnectionStrings:Default` on either engine.

Replace `<n>` with the actual service name in all templates.

---

## Microservice Architectures

### --simple (single project, direct data access)

**When to use**: Small services, CRUD APIs, internal tools, prototypes, lookup services.

**Structure**:
```
src/<n>.Api/
├── Endpoints/ (or Controllers/)
├── Services/
├── Data/
│   ├── AppDbContext.cs
│   └── Entities/
├── DTOs/
├── Middleware/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
└── <n>.Api.csproj

tests/<n>.Tests/
├── Endpoints/ (or Controllers/)
├── Services/
└── <n>.Tests.csproj
```

No MediatR, no pipeline behaviors, no Domain/Application/Infrastructure split. Service classes call EF Core/Dapper directly.

---

### --clean (4 projects, service classes, NO MediatR)

**When to use**: Medium complexity, proper layer separation, team collaboration, but CQRS overhead not justified.

**Structure**:
```
src/<n>.Domain/
├── Entities/
├── ValueObjects/
├── Repositories/           (interfaces only)
├── Exceptions/
└── <n>.Domain.csproj

src/<n>.Application/
├── Services/               (NOT Commands/Queries — plain service interfaces)
│   ├── IOrderService.cs
│   └── OrderService.cs
├── DTOs/
├── Validators/
├── Interfaces/
├── DependencyInjection.cs
└── <n>.Application.csproj

src/<n>.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Configurations/
│   └── Repositories/
├── ExternalServices/
├── DependencyInjection.cs
└── <n>.Infrastructure.csproj

src/<n>.Api/
├── Endpoints/ (or Controllers/)
├── Middleware/
├── Program.cs
├── Dockerfile
└── <n>.Api.csproj

tests/<n>.Unit.Tests/
tests/<n>.Integration.Tests/
tests/<n>.Architecture.Tests/
```

No MediatR, no pipeline behaviors. Endpoints/controllers call service interfaces. Services call repository interfaces. Infrastructure implements both.

---

### --cqrs (4 projects + commands/queries + decorators, no mediator library)

**When to use**: Complex business domains, multiple bounded contexts, event-driven architecture, need pipeline behaviors (validation, logging, transaction).

**Structure**:
```
src/<n>.Domain/
├── Entities/
├── ValueObjects/
├── Events/
├── Repositories/           (interfaces only)
├── Exceptions/
└── <n>.Domain.csproj

src/<n>.Application/
├── Commands/
│   └── CreateOrder/
│       ├── CreateOrderCommand.cs
│       ├── CreateOrderHandler.cs
│       └── CreateOrderValidator.cs
├── Queries/
│   └── GetOrderById/
│       ├── GetOrderByIdQuery.cs
│       └── GetOrderByIdHandler.cs
├── DTOs/
├── Cqrs/
│   ├── Contracts.cs              (ICommand, IQuery, handler interfaces, IUnitOfWork)
│   ├── Decorators.cs             (logging, validation, transaction)
│   └── CqrsRegistration.cs       (AddCqrsHandlers)
├── Interfaces/
├── DependencyInjection.cs
└── <n>.Application.csproj

src/<n>.Infrastructure/   (same as --clean)
src/<n>.Api/              (same as --clean)
tests/ (same as --clean + more behavior tests)
```

Command/query separation with plain handlers and decorators, as in the `cqrs-eventsourcing` skill (its
code is tested in CI): copy `Contracts.cs`, `Decorators.cs` and `CqrsRegistration.cs` from there. **No
MediatR** unless the developer asks for it by name (`package-policy`: commercial from 13.0).

---

## API Styles

### --minimal-api

**Program.cs wiring**:
```csharp
app.MapGroup("/api/orders")
    .MapOrderEndpoints()
    .RequireAuthorization()
    .WithTags("Orders");
```

**Endpoint file**:
```csharp
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAll).WithName("GetAllOrders");
        group.MapGet("/{id:int}", GetById).WithName("GetOrderById");
        group.MapPost("/", Create).WithName("CreateOrder");
        group.MapPut("/{id:int}", Update).WithName("UpdateOrder");
        group.MapDelete("/{id:int}", Delete).WithName("DeleteOrder");
        return group;
    }

    // handlers below...
}
```

### --controllers

**Program.cs wiring**:
```csharp
builder.Services.AddControllers();
// ...
app.MapControllers();
```

**Controller file**:
```csharp
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll(CancellationToken ct)
    { }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken ct)
    { }

    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    { }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(int id, UpdateOrderRequest request, CancellationToken ct)
    { }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    { }
}
```

---

## End-to-End Wiring (all 6 combinations)

### --simple --minimal-api
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));   // --sqlserver: UseSqlServer
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddOpenApi();
var app = builder.Build();
app.MapGroup("/api/orders").MapOrderEndpoints();
app.Run();

// Endpoints/OrderEndpoints.cs
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        return group;
    }

    private static async Task<Results<Ok<OrderDto>, NotFound>> GetById(
        int id, IOrderService service, CancellationToken ct)
    {
        var order = await service.GetByIdAsync(id, ct);
        return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
    }

    private static async Task<Created<OrderDto>> Create(
        CreateOrderRequest request, IOrderService service, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return TypedResults.Created($"/api/orders/{order.Id}", order);
    }
}

// Services/OrderService.cs
public sealed class OrderService(AppDbContext context, ICurrentUser currentUser) : IOrderService
{
    public async Task<OrderDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        // IDs are sequential ints in URLs — filter by the caller, or anyone can read any order (rules/security.md)
        var order = await context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == currentUser.CustomerId, ct);
        return order?.ToDto();
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var order = new Order { /* ... */ };   // Id: int identity, assigned by the database
        context.Orders.Add(order);
        await context.SaveChangesAsync(ct);
        return order.ToDto();
    }
}
```

### --simple --controllers
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));   // --sqlserver: UseSqlServer
builder.Services.AddScoped<IOrderService, OrderService>();
var app = builder.Build();
app.MapControllers();
app.Run();

// Controllers/OrdersController.cs
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken ct)
    {
        var order = await service.GetByIdAsync(id, ct);
        return order is not null ? Ok(order) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }
}
// OrderService.cs — same as simple --minimal-api
```

### --clean --minimal-api
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
var app = builder.Build();
app.MapGroup("/api/orders").MapOrderEndpoints().RequireAuthorization();
app.Run();

// Api/Endpoints/OrderEndpoints.cs
private static async Task<Results<Ok<OrderDto>, NotFound>> GetById(
    int id, IOrderService service, CancellationToken ct)
{
    var order = await service.GetByIdAsync(id, ct);
    return order is not null ? TypedResults.Ok(order) : TypedResults.NotFound();
}

// Application/Services/OrderService.cs
public sealed class OrderService(IOrderRepository repository, IUnitOfWork unitOfWork) : IOrderService
{
    public async Task<OrderDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var order = await repository.GetByIdAsync(id, ct);   // repository filters by the caller — ids are guessable ints
        return order?.ToDto();
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var order = Order.Create(request.CustomerId, request.Items);
        await repository.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }
}

// Domain/Repositories/IOrderRepository.cs
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
}

// Infrastructure/Persistence/Repositories/OrderRepository.cs
public sealed class OrderRepository(AppDbContext context) : IOrderRepository
{
    public async Task<Order?> GetByIdAsync(int id, CancellationToken ct)
        => await context.Orders.FindAsync([id], ct);

    public async Task AddAsync(Order order, CancellationToken ct)
        => await context.Orders.AddAsync(order, ct);
}
```

### --clean --controllers
```csharp
// Same as --clean --minimal-api but replace endpoints with:
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken ct)
    {
        var order = await service.GetByIdAsync(id, ct);
        return order is not null ? Ok(order) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }
}
// Service and repository layers — identical to --clean --minimal-api
```

### --cqrs --minimal-api
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
var app = builder.Build();
app.MapGroup("/api/orders").MapOrderEndpoints().RequireAuthorization();
app.Run();

// Api/Endpoints/OrderEndpoints.cs: each route asks for the handler it needs
private static async Task<Results<Ok<OrderDto>, NotFound>> GetById(
    int id, IQueryHandler<GetOrderByIdQuery, OrderDto?> handler, CancellationToken ct)
{
    var result = await handler.HandleAsync(new GetOrderByIdQuery(id), ct);
    return result is not null ? TypedResults.Ok(result) : TypedResults.NotFound();
}

private static async Task<Created<OrderDto>> Create(
    CreateOrderRequest request, ICommandHandler<CreateOrderCommand, OrderDto> handler, CancellationToken ct)
{
    var result = await handler.HandleAsync(new CreateOrderCommand(request.CustomerId, request.Items), ct);
    return TypedResults.Created($"/api/orders/{result.Id}", result);
}

// Application/Commands/CreateOrder/CreateOrderCommand.cs
public sealed record CreateOrderCommand(int CustomerId, List<OrderItemDto> Items) : ICommand<OrderDto>;

// Application/Commands/CreateOrder/CreateOrderHandler.cs
public sealed class CreateOrderHandler(IOrderRepository repository, AppDbContext db)
    : ICommandHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> HandleAsync(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Items.Select(/* ... */));
        await repository.AddAsync(order, ct);
        await db.SaveChangesAsync(ct);   // inside the TransactionDecorator
        return order.ToDto();
    }
}

// Application/Queries/GetOrderById/GetOrderByIdQuery.cs
public sealed record GetOrderByIdQuery(int Id) : IQuery<OrderDto?>;

// Application/DependencyInjection.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
    services.AddCqrsHandlers(typeof(DependencyInjection).Assembly);   // logging → validation → transaction → handler
    return services;
}
// Infrastructure/DependencyInjection.cs: services.AddScoped<IUnitOfWork, EfUnitOfWork>();
```

### --cqrs --controllers
```csharp
// Same as --cqrs --minimal-api but replace endpoints with:
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(
        int id, [FromServices] IQueryHandler<GetOrderByIdQuery, OrderDto?> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetOrderByIdQuery(id), ct);
        return result is not null ? Ok(result) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(
        CreateOrderRequest request, [FromServices] ICommandHandler<CreateOrderCommand, OrderDto> handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new CreateOrderCommand(request.CustomerId, request.Items), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
```


---

## Shared Across All Microservice Combinations

Every microservice scaffold also generates:
- **Dockerfile** (5-stage, `aspnet:10.0-noble-chiseled`, `.csproj`-first caching)
- **appsettings.json** + **appsettings.Development.json**
- **Health checks** (`/health/live`, `/health/ready`)
- **.editorconfig** reference
- **`public partial class Program;`** at bottom of `Program.cs` (for `WebApplicationFactory`)

For `--clean` and `--cqrs`, also generates:
- **DependencyInjection.cs** per layer
- **AppDbContext.cs** with `ApplyConfigurationsFromAssembly`
- **ApiFactory.cs** (Testcontainers + Respawn for the chosen engine, schema from SQL; `testing-integration` §1)
- **LayerDependencyTests.cs** (NetArchTest)

---

## Worker / Job / CronJob (unchanged)

These don't have the architecture × API style choice:

```
/scaffold worker <Name>      → BackgroundService + PeriodicTimer + health checks
/scaffold job <Name>         → Quartz.NET or Hangfire in long-running host
/scaffold cronjob <Name>     → Console app + K8s CronJob manifest
/scaffold endpoint <Name>    → Add endpoint/controller group to existing service
```

Reference skills: `worker-patterns`, `quartz-scheduling`, `hangfire-patterns`, `cronjob-patterns`.

---

## After Scaffolding

1. `dotnet restore && dotnet build`
2. Update `Directory.Build.props` if needed
3. For `--clean` and `--cqrs`: run `dotnet ef migrations add Initial -p Infrastructure -s Api`
4. For `--simple`: run `dotnet ef migrations add Initial`
   Then `/migrate script` for the up and down scripts. The app never runs `Migrate()`.
5. Run `/architecture-check` (for `--clean` and `--cqrs` only)
6. Start implementing with `/tdd`

### Auth services
Scaffolding a service that **issues** tokens (an OAuth2/OIDC authorization server — login, token,
introspection, revocation) uses the **`openiddict-server`** skill, not `auth-patterns`. The two are
complements: `openiddict-server` issues, `auth-patterns` validates. See the auth routing rule in
`rules/agents.md`.
