using System.Reflection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Combats.Contracts.Battle;
using Combats.Infrastructure.Messaging.Filters;
using Combats.Infrastructure.Messaging.Inbox;
using Combats.Infrastructure.Messaging.Naming;
using Combats.Infrastructure.Messaging.Options;
using KebabCaseEndpointNameFormatter = Combats.Infrastructure.Messaging.Naming.KebabCaseEndpointNameFormatter;

namespace Combats.Infrastructure.Messaging.DependencyInjection;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure = null)
    {
        // Bind and validate options
        var messagingSection = configuration.GetSection(MessagingOptions.SectionName);
        services.Configure<MessagingOptions>(messagingSection);
        services.AddOptions<MessagingOptions>().Bind(messagingSection).ValidateOnStart();

        var options = new MessagingOptions();
        messagingSection.Bind(options);
        if (string.IsNullOrWhiteSpace(options.RabbitMq.Host))
        {
            throw new InvalidOperationException(
                $"Messaging configuration section '{MessagingOptions.SectionName}' is missing or invalid");
        }

        ValidateRequiredOptions(options);

        // Build entity name map and get DbContext type
        var builder = new MessagingBuilder();
        
        // Apply canonical entity name mappings
        ApplyCanonicalEntityNames(builder);
        
        configure?.Invoke(builder);
        var entityNameMap = builder.GetEntityNameMap();
        var serviceDbContextType = builder.GetServiceDbContextType();

        // Validate DbContext requirements
        if (options.Outbox.Enabled && serviceDbContextType == null)
        {
            throw new InvalidOperationException(
                "Outbox is enabled but no service DbContext type is specified. " +
                "Call builder.WithServiceDbContext<T>() or builder.WithOutbox<T>() in the configure action.");
        }

        if (options.Inbox.Enabled && serviceDbContextType == null)
        {
            throw new InvalidOperationException(
                "Inbox is enabled but no service DbContext type is specified. " +
                "Call builder.WithServiceDbContext<T>() or builder.WithInbox<T>() in the configure action.");
        }

        // Register inbox services if enabled
        if (options.Inbox.Enabled && serviceDbContextType != null)
        {
            // Register IInboxStore (EF Core implementation)
            var storeType = typeof(InboxStore<>).MakeGenericType(serviceDbContextType);
            services.AddScoped(typeof(IInboxStore), storeType);

            // Register IInboxProcessor
            services.AddScoped<IInboxProcessor, InboxProcessor>();

            // Register IConsumerIdProvider (default implementation)
            services.AddSingleton<IConsumerIdProvider, ConsumerIdProvider>();

            // Register inbox retention cleanup service
            var serviceType = typeof(InboxRetentionCleanupService<>).MakeGenericType(serviceDbContextType);
            services.Add(ServiceDescriptor.Singleton(typeof(IHostedService), serviceType));
        }

        // Store entity name map in service collection for use by filters and formatters
        services.AddSingleton(entityNameMap);

        // Register MassTransit
        services.AddMassTransit(x =>
        {
            // Register consumers
            configureConsumers(x);

            // Configure bus factory
            x.UsingRabbitMq((context, cfg) =>
            {
                var messagingOptions = context.GetRequiredService<IOptions<MessagingOptions>>().Value;
                var entityNameMapInstance = context.GetRequiredService<Dictionary<Type, string>>();

                // Configure RabbitMQ host
                cfg.Host(messagingOptions.RabbitMq.Host, messagingOptions.RabbitMq.VirtualHost, h =>
                {
                    h.Username(messagingOptions.RabbitMq.Username);
                    h.Password(messagingOptions.RabbitMq.Password);
                    if (messagingOptions.RabbitMq.UseTls)
                    {
                        h.UseSsl(s => { });
                    }
                    h.Heartbeat(TimeSpan.FromSeconds(messagingOptions.RabbitMq.HeartbeatSeconds));
                });

                // Configure transport settings
                cfg.PrefetchCount = messagingOptions.Transport.PrefetchCount;
                cfg.ConcurrentMessageLimit = messagingOptions.Transport.ConcurrentMessageLimit;

                // Configure entity name formatter
                var entityNameFormatter = new EntityNameConvention(
                    entityNameMapInstance,
                    messagingOptions.Topology.EntityNamePrefix,
                    messagingOptions.Topology.UseKebabCase);
                cfg.MessageTopology.SetEntityNameFormatter(entityNameFormatter);

                // Configure retry policy
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(
                        messagingOptions.Retry.ExponentialCount,
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialMinMs),
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialMaxMs),
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialDeltaMs));
                });

                // Configure redelivery policy
                if (messagingOptions.Redelivery.Enabled)
                {
                    cfg.UseDelayedRedelivery(r =>
                    {
                        var intervals = messagingOptions.Redelivery.IntervalsSeconds
                            .Select(s => TimeSpan.FromSeconds(s))
                            .ToArray();
                        r.Intervals(intervals);
                    });
                }

                // Apply consume filters
                cfg.UseConsumeFilter(typeof(ConsumeLoggingFilter<>), context);

                // Configure endpoints
                var endpointFormatter = new KebabCaseEndpointNameFormatter(
                    serviceName, 
                    false, 
                    entityNameFormatter);
                
                cfg.ConfigureEndpoints(context, e =>
                {
                    // Configure outbox on each endpoint if enabled
                    if (messagingOptions.Outbox.Enabled && serviceDbContextType != null)
                    {
                        var method = typeof(MessagingServiceCollectionExtensions)
                            .GetMethod(nameof(ConfigureOutboxOnEndpoint), 
                                BindingFlags.NonPublic | BindingFlags.Static)
                            ?? throw new InvalidOperationException("Failed to find ConfigureOutboxOnEndpoint method");
                        var genericMethod = method.MakeGenericMethod(serviceDbContextType);
                        genericMethod.Invoke(null, [e, context, messagingOptions]);
                    }
                }, endpointFormatter);

                // Configure inbox filter if enabled
                if (messagingOptions.Inbox.Enabled && serviceDbContextType != null)
                {
                    // Register inbox filter - it will use IInboxProcessor which uses IInboxStore
                    cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);
                }

            });
        });

        return services;
    }

    private static void ValidateRequiredOptions(MessagingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RabbitMq.Host))
            throw new InvalidOperationException("Messaging:RabbitMq:Host is required");

        if (string.IsNullOrWhiteSpace(options.RabbitMq.Username))
            throw new InvalidOperationException("Messaging:RabbitMq:Username is required");

        if (string.IsNullOrWhiteSpace(options.RabbitMq.Password))
            throw new InvalidOperationException("Messaging:RabbitMq:Password is required");
    }

    private static void ConfigureOutboxOnEndpoint<TDbContext>(IReceiveEndpointConfigurator endpoint, IRegistrationContext context, MessagingOptions options)
        where TDbContext : DbContext
    {
        endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
    }


    private static void ApplyCanonicalEntityNames(MessagingBuilder builder)
    {
        // Apply canonical entity names from battle-contracts-v1.md
        // Battle.CreateBattle -> battle.create-battle
        // Battle.BattleCreated -> battle.battle-created
        // Battle.EndBattle -> battle.end-battle
        // Battle.BattleEnded -> battle.battle-ended
        
        builder.MapEntityName<CreateBattle>("battle.create-battle");
        builder.MapEntityName<BattleCreated>("battle.battle-created");
        builder.MapEntityName<EndBattle>("battle.end-battle");
        builder.MapEntityName<BattleEnded>("battle.battle-ended");
    }
}

// KebabCase endpoint name formatter