using System.Text.Json.Serialization;
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
using Serilog;
using Serilog.Events;
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

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext() ;
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

// Configure TurnDeadlineWorker options
builder.Services.Configure<TurnDeadlineWorkerOptions>(builder.Configuration.GetSection("Battle:TurnDeadlineWorker"));

// Validate BattleRulesetsOptions at startup (fail fast)
builder.Services.AddOptions<BattleRulesetsOptions>()
    .Bind(builder.Configuration.GetSection(BattleRulesetsOptions.SectionName))
    .Validate(options =>
    {
        if (options.CurrentVersion <= 0)
        {
            throw new InvalidOperationException($"Battle:Rulesets:CurrentVersion must be greater than 0. Current value: {options.CurrentVersion}");
        }

        if (!options.Versions.TryGetValue(options.CurrentVersion.ToString(), out var currentVersionConfig))
        {
            throw new InvalidOperationException($"Battle:Rulesets:CurrentVersion {options.CurrentVersion} not found in Battle:Rulesets:Versions. Available versions: {string.Join(", ", options.Versions.Keys)}");
        }

        if (currentVersionConfig.TurnSeconds <= 0)
        {
            throw new InvalidOperationException($"Battle:Rulesets:Versions:{options.CurrentVersion}:TurnSeconds must be greater than 0. Current value: {currentVersionConfig.TurnSeconds}");
        }

        if (currentVersionConfig.NoActionLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:NoActionLimit must be greater than 0. Current value: {currentVersionConfig.NoActionLimit}");
        }

        if (currentVersionConfig.CombatBalance == null)
        {
            throw new InvalidOperationException($"Battle:Rulesets:Versions:{options.CurrentVersion}:CombatBalance is required but is null.");
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

// Configure SignalR with JSON options to serialize enums as strings
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Configure messaging with typed DbContext for outbox/inbox support
builder.Services.AddMessaging<BattleDbContext>(
    builder.Configuration,
    "battle",
    x =>
    {
        x.AddConsumer<CreateBattleConsumer>();
        x.AddConsumer<BattleEndedProjectionConsumer>();
    },
    messagingBuilder =>
    {
        // Register entity name mappings (logical keys -> resolved from configuration)
        messagingBuilder.Map<CreateBattle>("CreateBattle");
        messagingBuilder.Map<BattleEnded>("BattleEnded");
    });

// Register turn deadline worker (background service for deadline-driven turn resolution)
builder.Services.AddHostedService<TurnDeadlineWorker>();

var app = builder.Build();

app.UseRouting();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, ex) =>
    {
        if (ex != null) return LogEventLevel.Error;
        var statusCode = httpContext.Response.StatusCode;
        return statusCode >= 500 ? LogEventLevel.Error
            : statusCode >= 400 ? LogEventLevel.Warning
            : LogEventLevel.Information;
    };
    
    options.EnrichDiagnosticContext = (diagContext, httpContext) =>
    {
        diagContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
        diagContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        diagContext.Set("TraceId", httpContext.TraceIdentifier);
    };
});

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

