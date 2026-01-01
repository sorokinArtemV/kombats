# Combats.Infrastructure.Messaging

Shared messaging infrastructure package for the Combats microservices architecture. This package provides a unified, production-ready messaging backbone using RabbitMQ + MassTransit with transactional outbox and inbox idempotency patterns.

## Features

- **Unified Configuration**: Single entry point for all messaging setup
- **Transactional Outbox**: Ensures message publishing is transactional with database changes
- **Inbox Idempotency**: Prevents duplicate message processing
- **Unified Retry/Redelivery**: Exponential retry + delayed redelivery + error queues
- **Stable Entity Names**: Canonical entity names independent of code namespaces
- **Kebab-case Naming**: Consistent queue and entity naming conventions
- **Structured Logging**: Comprehensive logging with correlation IDs
- **Automatic Cleanup**: Background service for inbox retention cleanup

## Quick Start

### 1. Install Package

Add a project reference to `Combats.Infrastructure.Messaging`:

```xml
<ProjectReference Include="..\Combats.Infrastructure.Messaging\Combats.Infrastructure.Messaging.csproj" />
```

### 2. Configure appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=my_service;Username=postgres;Password=postgres"
  },
  "Messaging": {
    "RabbitMq": {
      "Host": "localhost",
      "VirtualHost": "/",
      "Username": "guest",
      "Password": "guest",
      "UseTls": false,
      "HeartbeatSeconds": 30
    },
    "Transport": {
      "PrefetchCount": 32,
      "ConcurrentMessageLimit": 8
    },
    "Topology": {
      "EndpointPrefix": "myservice",
      "EntityNamePrefix": "combats",
      "UseKebabCase": true
    },
    "Retry": {
      "Mode": "Exponential",
      "ExponentialCount": 5,
      "ExponentialMinMs": 200,
      "ExponentialMaxMs": 5000,
      "ExponentialDeltaMs": 200
    },
    "Redelivery": {
      "Enabled": true,
      "IntervalsSeconds": [30, 120, 600]
    },
    "Outbox": {
      "Enabled": true,
      "QueryDelaySeconds": 1,
      "DeliveryLimit": 500
    },
    "Inbox": {
      "Enabled": true,
      "RetentionDays": 7,
      "CleanupIntervalMinutes": 15
    }
  }
}
```

### 3. Register DbContext

Register your service's DbContext before calling `AddMessaging`:

```csharp
services.AddDbContext<MyServiceDbContext>(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});
```

### 4. Configure Messaging

In your `Program.cs` or `Startup.cs`:

```csharp
using Combats.Infrastructure.Messaging.DependencyInjection;
using Combats.Contracts.MyService;

services.AddMessaging(
    configuration,
    "myservice", // Service name used in queue naming
    x => x.AddConsumer<MyCommandConsumer>(), // Register your consumers
    builder =>
    {
        // Map entity names to canonical names (optional but recommended)
        builder.MapEntityName<MyCommand>("myservice.my-command");
        builder.MapEntityName<MyEvent>("myservice.my-event");
        
        // Specify service DbContext for outbox (required if outbox is enabled)
        builder.WithServiceDbContext<MyServiceDbContext>();
    });
```

### 5. Create Migrations

Create migrations for:
- Your service's DbContext (includes outbox tables when outbox is enabled)
- InboxDbContext (automatically registered when inbox is enabled)

**Important**: Create migrations in this order:

1. First, create your domain model migration:
```bash
dotnet ef migrations add InitialCreate --project src/MyService --context MyServiceDbContext
```

2. After calling `AddMessaging`, create a migration that includes outbox tables:
```bash
dotnet ef migrations add AddOutbox --project src/MyService --context MyServiceDbContext
```

3. Create inbox migration:
```bash
dotnet ef migrations add InboxMessages --project src/MyService --context InboxDbContext
```

**Note**: MassTransit's outbox tables are automatically added to your DbContext model when `UseEntityFrameworkOutbox<T>` is called. The migration will include tables like `InboxState` and `OutboxMessage`.

### 6. Apply Migrations

```csharp
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MyServiceDbContext>();
    await dbContext.Database.MigrateAsync();
    
    var inboxDbContext = scope.ServiceProvider.GetService<InboxDbContext>();
    if (inboxDbContext != null)
    {
        await inboxDbContext.Database.MigrateAsync();
    }
}
```

## Consumer Implementation

### Basic Consumer

```csharp
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Combats.Contracts.MyService;

public class MyCommandConsumer : IConsumer<MyCommand>
{
    private readonly MyServiceDbContext _dbContext;
    private readonly ILogger<MyCommandConsumer> _logger;

    public MyCommandConsumer(
        MyServiceDbContext dbContext,
        ILogger<MyCommandConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MyCommand> context)
    {
        var command = context.Message;
        
        // Your business logic here
        // ...
        
        // Publish events via ConsumeContext (required for outbox)
        await context.Publish(new MyEvent { /* ... */ }, context.CancellationToken);
        
        // Save changes (outbox will publish events transactionally)
        await _dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
```

### Idempotent Consumer

Consumers should be idempotent. The inbox filter provides MessageId-level idempotency, but you should also implement business-level idempotency:

```csharp
public async Task Consume(ConsumeContext<CreateOrder> context)
{
    var command = context.Message;
    
    // Business-level idempotency check
    var existing = await _dbContext.Orders
        .FirstOrDefaultAsync(o => o.OrderId == command.OrderId, context.CancellationToken);
    
    if (existing != null)
    {
        _logger.LogInformation("Order {OrderId} already exists, skipping", command.OrderId);
        return; // ACK without side effects
    }
    
    // Create order...
}
```

## Entity Name Mapping

Entity names (exchange names) must be stable and independent of code namespaces. Map them explicitly:

```csharp
builder.MapEntityName<MyCommand>("myservice.my-command");
builder.MapEntityName<MyEvent>("myservice.my-event");
```

If not mapped, the package will generate names using kebab-case conversion of the type name with the configured prefix.

## Queue Naming

Queues are automatically named using the pattern: `{service}.{endpoint}` in kebab-case.

Examples:
- Service: `battle`, Consumer: `CreateBattleConsumer` → Queue: `battle.create-battle`
- Service: `matchmaking`, Consumer: `SagaConsumer` → Queue: `matchmaking.saga`

## Retry and Redelivery

The package configures a unified retry/redelivery policy:

- **Retry**: Exponential backoff, 5 attempts, 200ms → 5s
- **Redelivery**: Delayed redelivery at 30s, 2m, 10m
- **Error Queue**: Messages that exhaust retries go to `{endpoint}_error`

**Important**: Do not implement custom retry logic in consumers. The package handles all retries.

## Outbox Pattern

When `Outbox.Enabled = true`:

1. Messages published via `ConsumeContext.Publish()` are stored in the outbox table
2. Outbox messages are published transactionally with `SaveChangesAsync()`
3. A background process delivers outbox messages to RabbitMQ

**Requirements**:
- Use `ConsumeContext.Publish()` (not `IPublishEndpoint`)
- Call `SaveChangesAsync()` after publishing
- Specify DbContext type via `builder.WithServiceDbContext<T>()`

## Inbox Pattern

When `Inbox.Enabled = true`:

1. Inbox filter checks MessageId before processing
2. If MessageId already processed → ACK without invoking handler
3. If handler fails → row is deleted to allow retry
4. Background service cleans up expired rows

**Requirements**:
- Provide `DefaultConnection` connection string
- Apply migrations for `InboxDbContext`

## Configuration Override via Environment Variables

Override configuration using environment variables:

```bash
export Messaging__RabbitMq__Host=rabbitmq.production
export Messaging__RabbitMq__Username=myuser
export Messaging__RabbitMq__Password=mypassword
export ConnectionStrings__DefaultConnection="Host=db.production;..."
```

## Architecture Compliance

This package enforces the architecture defined in:
- `docs/architecture/messaging.md`
- `docs/architecture/messaging-package.md`
- `docs/architecture/messaging-storage.md`

All services MUST use this package for messaging configuration. Manual MassTransit configuration is not allowed.

## Testing

### Testing Idempotency

To verify that your consumers are idempotent:

1. **Business-level idempotency**: Check that processing the same message twice doesn't create duplicate entities
2. **Inbox-level idempotency**: Verify that the inbox filter prevents duplicate processing

Example test scenario for `CreateBattleConsumer`:

```csharp
// Send CreateBattle command with BattleId = "123"
// Verify battle is created

// Send the same CreateBattle command again (same BattleId)
// Verify:
// - No duplicate battle is created
// - Inbox table has one row with status='processed'
// - Consumer logs show "already exists, skipping"
```

### Testing with MassTransit Test Harness

For integration testing, use MassTransit's test harness:

```csharp
var harness = new InMemoryTestHarness();
var consumerHarness = harness.Consumer<MyConsumer>();

await harness.Start();

try
{
    await harness.InputQueueSendEndpoint.Send(new MyCommand { /* ... */ });
    
    // Assert consumer was called
    Assert.True(await consumerHarness.Consumed.Any<MyCommand>());
}
finally
{
    await harness.Stop();
}
```

## Troubleshooting

### Outbox not publishing messages

- Ensure `Outbox.Enabled = true` in configuration
- Verify DbContext is registered before `AddMessaging`
- Call `builder.WithServiceDbContext<T>()` in the configure action
- Use `ConsumeContext.Publish()`, not `IPublishEndpoint.Publish()`
- Call `SaveChangesAsync()` after publishing

### Inbox not preventing duplicates

- Ensure `Inbox.Enabled = true` in configuration
- Verify `DefaultConnection` connection string is configured
- Check that `InboxDbContext` migrations are applied
- Verify the inbox filter is applied (check logs)

### Messages not being consumed

- Verify RabbitMQ connection settings
- Check queue names match expected pattern
- Ensure consumers are registered in `configureConsumers`
- Check MassTransit logs for connection errors

## License

Internal use only - Combats project.

