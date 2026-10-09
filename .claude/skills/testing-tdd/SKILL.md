---
name: testing-tdd
description: TDD for .NET: red-green-refactor, xUnit + Moq + FluentAssertions + AutoFixture, AAA, test naming. Test behavior, not implementation.
version: 1.0.0
---

# TDD & Unit Testing Patterns

## The default unit-testing setup

Where your team's own standard differs, the team's standard wins.

- **Stack:** xUnit + **Moq or NSubstitute** (whichever the codebase uses; never both in one test
  project) + FluentAssertions **7.x only — never 8+** (`.Should()`) + AutoFixture + Coverlet.
  **FluentAssertions pin:** 7.x is the last Apache-2.0 version; 8.0+ needs a paid Xceed license for
  commercial use. Pin the range in every test project: `<PackageReference Include="FluentAssertions"
  Version="[7.0.0,8.0.0)" />` so a package bump can't silently put a commercial project in breach.
- **AAA** (Arrange-Act-Assert) in every test.
- **Test naming:** `MethodName_Scenario_ExpectedResult` (e.g. `PlaceOrder_WhenOutOfStock_ReturnsFalse`).
- **Test project:** one project per service named `{Solution}.Test` under `Tests/`, referencing **all**
  source projects; mirror the source folder structure. **Multiple test projects are also fine** —
  coverage tools such as SonarQube merge coverage **by source file**, so two test projects touching the
  same assembly do **not** double-count. Single project is simplest; split
  only when you have a real reason.
- **Coverage:** one-test-per-branch is the efficient path. Coverlet collects; a quality gate such as
  SonarQube's counts **branch conditions as well as lines**, often on **new code only**.
- **What NOT to test:** private methods (test through public), trivial getters/setters, EF migrations,
  DbContext, DI registration. Don't add tests just to inflate coverage.
- **Exclude Migrations from coverage** via **`sonar.coverage.exclusions`** (Sonar-side, in the scanner
  config) — NOT a repo `.runsettings`, because the pipeline forces opencover globally and doesn't honor
  a repo `.runsettings` for collection.
- **.NET 10 CI bridge:** if the test project still targets `net8.0` while CI runs the .NET 10 runtime,
  add `<RollForward>Major</RollForward>` to the test `.csproj`. Remove it once the project is on
  `net10.0`.
- **Writing and maintaining tests with AI is fine** and a good way to reach coverage targets, as long as
  every test meets the same bar as a hand-written one.

Moq quick reference: `new Mock<IRepo>()`,
`mock.Setup(r => r.GetAsync(It.IsAny<int>())).ReturnsAsync(value)`, `mock.Object` to inject,
`mock.Verify(r => r.SaveAsync(It.IsAny<Order>()), Times.Once)` for critical side-effects.

## Test Project Structure

```
tests/
├── OrderService.Unit.Tests/           # mirrors src/OrderService.Domain + Application
│   ├── Domain/
│   │   ├── Orders/
│   │   │   ├── OrderTests.cs
│   │   │   └── MoneyTests.cs
│   │   └── Shared/
│   ├── Application/
│   │   ├── Orders/
│   │   │   ├── CreateOrderHandlerTests.cs
│   │   │   └── CreateOrderValidatorTests.cs
│   │   └── Behaviors/
│   └── GlobalUsings.cs
├── OrderService.Integration.Tests/     # see testing-integration skill
└── OrderService.Architecture.Tests/    # see testing-architecture skill
```

### Global Usings for Test Projects
```csharp
// GlobalUsings.cs
global using Xunit;
global using FluentAssertions;
global using Moq;
global using AutoFixture;
global using AutoFixture.AutoMoq;
global using AutoFixture.Xunit2;
```

## Naming Convention

`MethodName_Scenario_ExpectedBehavior`

```csharp
public sealed class OrderTests
{
    [Fact]
    public void Create_ValidItems_ReturnsOrderWithPendingStatus() { }

    [Fact]
    public void Create_EmptyItems_ThrowsDomainException() { }

    [Fact]
    public void Ship_WhenProcessing_ChangesStatusToShipped() { }

    [Fact]
    public void Ship_WhenPending_ThrowsDomainException() { }

    [Fact]
    public void Cancel_WhenShipped_ThrowsDomainException() { }

    [Fact]
    public void AddLineItem_DuplicateProduct_IncreasesQuantity() { }
}
```

## Arrange-Act-Assert

```csharp
[Fact]
public void Create_ValidItems_ReturnsOrderWithCorrectTotal()
{
    // Arrange
    var timeProvider = new FakeTimeProvider(
        new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
    var items = new List<OrderLineItem>
    {
        OrderLineItem.Create(0, 7, "Widget", 2,
            new Money(10.00m, "SAR")),
        OrderLineItem.Create(0, 8, "Gadget", 1,
            new Money(25.00m, "SAR"))
    };

    // Act
    var order = Order.Create(1, 42, items, timeProvider);

    // Assert
    order.Status.Should().Be(OrderStatus.Pending);
    order.Total.Should().Be(new Money(45.00m, "SAR"));
    order.LineItems.Should().HaveCount(2);
    order.CreatedAt.Should().Be(timeProvider.GetUtcNow());
}
```

## Theory with InlineData and MemberData

```csharp
[Theory]
[InlineData(0)]
[InlineData(-1)]
[InlineData(-100)]
public void Create_InvalidQuantity_ThrowsDomainException(int quantity)
{
    // Arrange & Act
    var act = () => OrderLineItem.Create(
        0, 7, "Widget", quantity, new Money(10m, "SAR"));

    // Assert
    act.Should().Throw<DomainException>()
        .WithMessage("*quantity*");
}

[Theory]
[MemberData(nameof(InvalidOrderTestCases))]
public void Create_InvalidInput_ThrowsDomainException(
    List<OrderLineItem> items, string expectedError)
{
    var act = () => Order.Create(1, 42, items, TimeProvider.System);

    act.Should().Throw<DomainException>()
        .WithMessage($"*{expectedError}*");
}

public static TheoryData<List<OrderLineItem>, string> InvalidOrderTestCases => new()
{
    { [], "at least one line item" },
    { null!, "at least one line item" }
};
```

## Moq Patterns

```csharp
[Fact]
public async Task Handle_OrderExists_ReturnsOrderDto()
{
    // Arrange
    var orderId = 1001;
    var order = CreateTestOrder(orderId);

    var repository = new Mock<IOrderRepository>();
    repository
        .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(order);

    var handler = new GetOrderByIdHandler(repository.Object);   // inject .Object

    // Act
    var result = await handler.Handle(
        new GetOrderByIdQuery(orderId), CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result!.Id.Should().Be(orderId);

    // Verify interactions
    repository.Verify(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
    repository.Verify(r => r.GetByCustomerAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
}

// Argument matching
repository
    .Setup(r => r.GetByIdAsync(It.Is<int>(id => id > 0), It.IsAny<CancellationToken>()))
    .ReturnsAsync(order);

// Throwing on specific calls
repository
    .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ThrowsAsync(new InvalidOperationException("DB unavailable"));

// Capturing arguments
var capturedId = 0;
repository
    .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .Callback<int, CancellationToken>((id, _) => capturedId = id)
    .ReturnsAsync(order);

// Sequential returns — first call null, second returns the order
repository
    .SetupSequence(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
    .ReturnsAsync((Order?)null)
    .ReturnsAsync(order);

// Strict mocks when an unexpected call should fail the test
var strict = new Mock<IOrderRepository>(MockBehavior.Strict);
```

## AutoFixture + Moq

```csharp
// Custom attribute for auto-mocked tests (AutoFixture.AutoMoq)
public sealed class AutoMoqDataAttribute : AutoDataAttribute
{
    public AutoMoqDataAttribute()
        : base(() => new Fixture().Customize(new AutoMoqCustomization())) { }
}

// Usage — AutoFixture generates all parameters, Moq mocks the interfaces.
// Freeze Mock<T> (not T) so you can both inject and verify on the same instance.
[Theory, AutoMoqData]
public async Task Handle_ValidCommand_CreatesOrder(
    [Frozen] Mock<IOrderRepository> repository,
    [Frozen] Mock<IUnitOfWork> unitOfWork,
    CreateOrderHandler sut,
    CreateOrderCommand command)
{
    // Arrange — AutoFixture created everything; [Frozen] ensures the sut got these mocks

    // Act
    var result = await sut.Handle(command, CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    repository.Verify(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
    unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
}
```

## FluentAssertions Common Patterns

```csharp
// Objects
result.Should().NotBeNull();
result.Should().BeEquivalentTo(expected, options => options
    .Excluding(o => o.CreatedAt)
    .Using<DateTimeOffset>(ctx =>
        ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(1)))
    .WhenTypeIs<DateTimeOffset>());

// Collections
orders.Should().HaveCount(3);
orders.Should().ContainSingle(o => o.Status == OrderStatus.Pending);
orders.Should().BeInDescendingOrder(o => o.CreatedAt);
orders.Should().AllSatisfy(o => o.CustomerId.Should().Be(customerId));
orders.Should().NotContain(o => o.IsDeleted);

// Exceptions
var act = () => order.Ship(TimeProvider.System);
act.Should().Throw<DomainException>()
    .WithMessage("Cannot ship order in * status");

// Async exceptions
var act = async () => await handler.Handle(command, CancellationToken.None);
await act.Should().ThrowAsync<ValidationException>()
    .Where(ex => ex.Errors.Any(e => e.PropertyName == "Items"));

// Execution time
var act = () => service.ProcessAsync(data, CancellationToken.None);
await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));

// String
result.Name.Should().StartWith("Order-");
result.Email.Should().Contain("@").And.EndWith(".com");
```

## Testing Domain Events

```csharp
[Fact]
public void Create_ValidOrder_RaisesOrderCreatedEvent()
{
    // Act
    var order = Order.Create(customerId, items, timeProvider);

    // Assert
    order.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<OrderCreatedEvent>()
        .Which.Should().Match<OrderCreatedEvent>(e =>
            e.OrderId == order.Id &&
            e.CustomerId == customerId);
}

[Fact]
public void Cancel_PendingOrder_RaisesOrderCancelledEvent()
{
    var order = CreatePendingOrder();

    order.Cancel("Customer requested");

    order.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<OrderCancelledEvent>()
        .Which.Reason.Should().Be("Customer requested");
}
```

## Testing Validators

```csharp
[Fact]
public async Task Validate_EmptyItems_HasValidationError()
{
    var validator = new CreateOrderCommandValidator();
    var command = new CreateOrderCommand(Items: [], Notes: null);

    var result = await validator.ValidateAsync(command);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle()
        .Which.PropertyName.Should().Be("Items");
}

[Fact]
public async Task Validate_ValidCommand_IsValid()
{
    var validator = new CreateOrderCommandValidator();
    var command = new CreateOrderCommand(
        Items: [new(7, 2)],
        Notes: "Rush order");

    var result = await validator.ValidateAsync(command);

    result.IsValid.Should().BeTrue();
}
```
