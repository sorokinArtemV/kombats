using Combats.Battle.Application.Protocol;
using Combats.Battle.Application.Rules;
using Combats.Battle.Api.Hubs;
using Combats.Battle.Api.Middleware;
using Combats.Battle.Api.Realtime;
using Combats.Battle.Infrastructure.Realtime.SignalR;
using Microsoft.AspNetCore.SignalR;
using Combats.Battle.Api.Workers;
using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.Services;
using Combats.Battle.Domain;
using Combats.Battle.Domain.Engine;
using Combats.Battle.Infrastructure.Messaging.Consumers;
using Combats.Battle.Infrastructure.Messaging;
using MassTransitBattleEventPublisher = Combats.Battle.Infrastructure.Messaging.Publisher.MassTransitBattleEventPublisher;
using Combats.Battle.Infrastructure.Persistence.EF;
using Combats.Battle.Infrastructure.Persistence.EF.DbContext;
using Combats.Battle.Infrastructure.Persistence.EF.Projections;
using Combats.Battle.Infrastructure.Profiles;
using Combats.Battle.Infrastructure.State.Redis;
using Combats.Battle.Infrastructure.Time;
using Combats.Infrastructure.Messaging.DependencyInjection;
using Combats.Contracts.Battle;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

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

// Register Domain
builder.Services.AddScoped<IBattleEngine, BattleEngine>();

// Register Application ports (implemented by Infrastructure)
builder.Services.AddScoped<IBattleStateStore, RedisBattleStateStore>();
// SignalRBattleRealtimeNotifier uses IHubContext<Hub> - provide adapter from IHubContext<BattleHub>
builder.Services.AddScoped<IBattleRealtimeNotifier>(sp =>
{
    var battleHubContext = sp.GetRequiredService<IHubContext<BattleHub>>();
    var hubContext = new HubContextAdapter(battleHubContext);
    var logger = sp.GetRequiredService<ILogger<SignalRBattleRealtimeNotifier>>();
    return new SignalRBattleRealtimeNotifier(hubContext, logger);
});
builder.Services.AddScoped<IBattleEventPublisher, MassTransitBattleEventPublisher>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICombatProfileProvider, DatabaseCombatProfileProvider>();

// Register Application services
builder.Services.AddSingleton<BattleRulesDefaults>();
builder.Services.AddScoped<RulesetNormalizer>();
builder.Services.AddScoped<PlayerActionNormalizer>();
builder.Services.AddScoped<BattleLifecycleAppService>();
builder.Services.AddScoped<BattleTurnAppService>();

// Configure SignalR
builder.Services.AddSignalR();

// Configure messaging with typed DbContext for outbox/inbox support
builder.Services.AddMessaging<BattleDbContext>(
    builder.Configuration,
    "battle",
    x =>
    {
        x.AddConsumer<CreateBattleConsumer>();
        x.AddConsumer<EndBattleConsumer>();
        x.AddConsumer<BattleEndedProjectionConsumer>();
    },
    messagingBuilder =>
    {
        // Register entity name mappings (logical keys -> resolved from configuration)
        messagingBuilder.Map<CreateBattle>("CreateBattle");
        // BattleCreated is published but not consumed internally (state initialization done directly in CreateBattleConsumer)
        messagingBuilder.Map<BattleCreated>("BattleCreated");
        messagingBuilder.Map<EndBattle>("EndBattle");
        messagingBuilder.Map<BattleEnded>("BattleEnded");
    });

// Register turn deadline worker (background service for deadline-driven turn resolution)
builder.Services.AddHostedService<TurnDeadlineWorker>();

var app = builder.Build();

app.UseRouting();

app.UseCors("AllowAll");

// DEV-ONLY: Add dev SignalR auth middleware (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<DevSignalRAuthMiddleware>();
}

// Configure the HTTP request pipeline
app.UseHttpsRedirection();

// Enable static files for dev UI
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<Combats.Battle.Api.Hubs.BattleHub>("/battlehub");

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles, player_profiles, and inbox/outbox tables)
    await dbContext.Database.MigrateAsync();
}

app.Run();

