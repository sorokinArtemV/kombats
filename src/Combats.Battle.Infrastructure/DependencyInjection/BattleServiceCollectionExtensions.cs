using Combats.Battle.Application.Abstractions;
using Combats.Battle.Application.UseCases.Lifecycle;
using Combats.Battle.Application.UseCases.Turns;
using Combats.Battle.Domain;
using Combats.Battle.Domain.Engine;
using Combats.Battle.Infrastructure.Messaging;
using Combats.Battle.Infrastructure.Messaging.Consumers;
using Combats.Battle.Infrastructure.Persistence.EF;
using Combats.Battle.Infrastructure.Persistence.EF.DbContext;
using Combats.Battle.Infrastructure.Persistence.EF.Projections;
using Combats.Battle.Infrastructure.Messaging;
using Combats.Battle.Infrastructure.Profiles;
using Combats.Battle.Infrastructure.Realtime.SignalR;
using Combats.Battle.Infrastructure.State.Redis;
using Combats.Battle.Infrastructure.Time;
using Combats.Battle.Infrastructure.Workers;
using Combats.Contracts.Battle;
using Combats.Infrastructure.Messaging.DependencyInjection;
using MassTransitBattleEventPublisher = Combats.Battle.Infrastructure.Messaging.Publisher.MassTransitBattleEventPublisher;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Combats.Battle.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Battle services in dependency injection.
/// These methods are shared between Api and Worker hosts to ensure consistent registration.
/// </summary>
public static class BattleServiceCollectionExtensions
{
    /// <summary>
    /// Registers Application layer services (use cases, validators, policies, abstractions).
    /// </summary>
    public static IServiceCollection AddBattleApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Domain
        services.AddScoped<IBattleEngine, BattleEngine>();

        // Register Application services
        services.AddSingleton<BattleRulesDefaults>();
        services.AddScoped<RulesetNormalizer>();
        services.AddScoped<PlayerActionNormalizer>();
        services.AddScoped<BattleLifecycleAppService>();
        services.AddScoped<BattleTurnAppService>();

        // Register Application abstractions (ports) - implementations registered in AddBattleInfrastructure
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>
    /// Registers Infrastructure layer services (Redis, EF, MassTransit, SignalR adapters).
    /// </summary>
    public static IServiceCollection AddBattleInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure PostgreSQL DbContext for Battle service
        services.AddDbContext<BattleDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection connection string is required");
            options.UseNpgsql(connectionString);
        });

        // Configure Redis
        var redisConnectionString = configuration.GetConnectionString("Redis")
                                   ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            return ConnectionMultiplexer.Connect(redisConnectionString);
        });

        // Configure Battle Redis options
        services.Configure<BattleRedisOptions>(
            configuration.GetSection(BattleRedisOptions.SectionName));

        // Register Application ports (implemented by Infrastructure)
        services.AddScoped<IBattleStateStore, RedisBattleStateStore>();
        services.AddScoped<ICombatProfileProvider, DatabaseCombatProfileProvider>();

        // Configure messaging with typed DbContext for outbox/inbox support
        services.AddMessaging<BattleDbContext>(
            configuration,
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

        return services;
    }

    /// <summary>
    /// Registers API-specific services (SignalR hubs, realtime notifier, controllers, middleware).
    /// This should only be called from the Api host, not the Worker host.
    /// </summary>
    public static IServiceCollection AddBattleApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure SignalR
        services.AddSignalR();

        // Register SignalR realtime notifier (requires IHubContext from SignalR)
        services.AddScoped<IBattleRealtimeNotifier, SignalRBattleRealtimeNotifier>();

        // Register MassTransit event publisher
        services.AddScoped<IBattleEventPublisher, MassTransitBattleEventPublisher>();

        // Add controllers
        services.AddControllers();

        return services;
    }

    /// <summary>
    /// Registers background workers (TurnDeadlineWorker, etc.).
    /// This can be called from either Api or Worker host, but typically only from Worker.
    /// </summary>
    public static IServiceCollection AddBattleWorkers(
        this IServiceCollection services)
    {
        services.AddHostedService<TurnDeadlineWorker>();
        return services;
    }
}

