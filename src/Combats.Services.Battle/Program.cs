using Microsoft.EntityFrameworkCore;
using Combats.Infrastructure.Messaging.DependencyInjection;
using Combats.Contracts.Battle;
using Combats.Services.Battle.Consumers;
using Combats.Services.Battle.Data;
using Combats.Services.Battle.State;
using Combats.Services.Battle.Services;
using Combats.Services.Battle.Middleware;
using Combats.Services.Battle.Hubs;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure PostgreSQL DbContext for Battle service
builder.Services.AddDbContext<BattleDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection connection string is required");
    options.UseNpgsql(connectionString);
});

// Configure Redis
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                           ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    return ConnectionMultiplexer.Connect(redisConnectionString);
});

// Register Battle State Store
builder.Services.AddScoped<IBattleStateStore, RedisBattleStateStore>();

// Configure SignalR
builder.Services.AddSignalR();

// Configure messaging with typed DbContext for outbox/inbox support
builder.Services.AddMessaging<BattleDbContext>(
    builder.Configuration,
    "battle",
    x =>
    {
        x.AddConsumer<CreateBattleConsumer>();
        x.AddConsumer<BattleCreatedEngineConsumer>();
        x.AddConsumer<ResolveTurnConsumer>();
        x.AddConsumer<EndBattleConsumer>();
    },
    messagingBuilder =>
    {
        // Register entity name mappings (logical keys -> resolved from configuration)
        messagingBuilder.Map<CreateBattle>("CreateBattle");
        messagingBuilder.Map<BattleCreated>("BattleCreated");
        messagingBuilder.Map<ResolveTurn>("ResolveTurn");
        messagingBuilder.Map<EndBattle>("EndBattle");
        messagingBuilder.Map<BattleEnded>("BattleEnded");
    });

// Register watchdog service (background service for recovering missing ResolveTurn schedules)
builder.Services.AddHostedService<BattleWatchdogService>();

var app = builder.Build();

// DEV-ONLY: Add dev SignalR auth middleware (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<DevSignalRAuthMiddleware>();
}

// Configure the HTTP request pipeline
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<BattleHub>("/battlehub");

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles and inbox_messages tables)
    await dbContext.Database.MigrateAsync();
}

app.Run();
