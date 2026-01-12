using Combats.Infrastructure.Messaging.DependencyInjection;
using Kombats.Battle.Application.UseCases.Lifecycle;
using Kombats.Battle.Application.UseCases.Turns;
using Kombats.Battle.Api.Middleware;
using Kombats.Battle.Infrastructure.Realtime.SignalR;
using Kombats.Battle.Api.Workers;
using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Domain.Engine;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Infrastructure.Messaging.Consumers;
using Kombats.Battle.Infrastructure.Messaging.Publisher;
using Kombats.Battle.Infrastructure.Persistence.EF.DbContext;
using Kombats.Battle.Infrastructure.Persistence.EF.Projections;
using Kombats.Battle.Infrastructure.Profiles;
using Kombats.Battle.Infrastructure.Rules;
using Kombats.Battle.Infrastructure.State.Redis;
using Kombats.Battle.Infrastructure.Time;

using Kombats.Contracts.Battle;
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
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

// Configure Battle Redis options
builder.Services.Configure<BattleRedisOptions>(builder.Configuration.GetSection(BattleRedisOptions.SectionName));

// Configure Battle Rulesets options (versioned rulesets from appsettings)
builder.Services.Configure<BattleRulesetsOptions>(builder.Configuration.GetSection(BattleRulesetsOptions.SectionName));

// Validate BattleRulesetsOptions at startup (fail fast)
builder.Services.AddOptions<BattleRulesetsOptions>()
    .Bind(builder.Configuration.GetSection(BattleRulesetsOptions.SectionName))
    .Validate(options =>
    {
        if (options.CurrentVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:CurrentVersion must be greater than 0. Current value: {options.CurrentVersion}");
        }

        if (!options.Versions.TryGetValue(options.CurrentVersion.ToString(), out var currentVersionConfig))
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:CurrentVersion {options.CurrentVersion} not found in Battle:Rulesets:Versions. " +
                $"Available versions: {string.Join(", ", options.Versions.Keys)}");
        }

        if (currentVersionConfig.TurnSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:TurnSeconds must be greater than 0. " +
                $"Current value: {currentVersionConfig.TurnSeconds}");
        }

        if (currentVersionConfig.NoActionLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:NoActionLimit must be greater than 0. " +
                $"Current value: {currentVersionConfig.NoActionLimit}");
        }

        if (currentVersionConfig.CombatBalance == null)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:CombatBalance is required but is null.");
        }

        return true;
    })
    .ValidateOnStart();

// Register Domain
builder.Services.AddSingleton<IRandomProvider, SystemRandomProvider>();
builder.Services.AddScoped<IBattleEngine, BattleEngine>();

// Register Application ports (implemented by Infrastructure)
builder.Services.AddScoped<IBattleStateStore, RedisBattleStateStore>();
// SignalRBattleRealtimeNotifier uses IHubContext<BattleHub> directly
builder.Services.AddScoped<IBattleRealtimeNotifier, SignalRBattleRealtimeNotifier>();
builder.Services.AddScoped<IBattleEventPublisher, MassTransitBattleEventPublisher>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICombatProfileProvider, DatabaseCombatProfileProvider>();

// Register ruleset and seed providers
builder.Services.AddScoped<IRulesetProvider, RulesetProvider>();
builder.Services.AddSingleton<ISeedGenerator, SeedGenerator>();

// Register Application services
builder.Services.AddSingleton<BattleRulesDefaults>();
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

// Map SignalR hub (now in Infrastructure)
app.MapHub<BattleHub>("/battlehub");

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles, player_profiles, and inbox/outbox tables)
    await dbContext.Database.MigrateAsync();
}

app.Run();

