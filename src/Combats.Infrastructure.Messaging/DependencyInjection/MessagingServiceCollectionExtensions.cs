using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Combats.Infrastructure.Messaging.Filters;
using Combats.Infrastructure.Messaging.Inbox;
using Combats.Infrastructure.Messaging.Naming;
using Combats.Infrastructure.Messaging.Options;
using KebabCaseEndpointNameFormatter = Combats.Infrastructure.Messaging.Naming.KebabCaseEndpointNameFormatter;

namespace Combats.Infrastructure.Messaging.DependencyInjection;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds MassTransit messaging infrastructure with compile-time generic DbContext support.
    /// Use this overload when you need Outbox or Inbox features (they require DbContext type at compile time).
    /// </summary>
    /// <typeparam name="TDbContext">The service's DbContext type for outbox/inbox persistence</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration root</param>
    /// <param name="serviceName">Service name used in endpoint naming (e.g., "battle", "matchmaking")</param>
    /// <param name="configureConsumers">Action to register MassTransit consumers</param>
    /// <param name="configure">Optional action to configure entity name mappings and DbContext</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure = null)
        where TDbContext : DbContext
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

        // Build entity name map
        var builder = new MessagingBuilder(configuration);
        configure?.Invoke(builder);
        var entityNameMap = builder.BuildEntityNameMap();

        // Register inbox services if enabled
        if (options.Inbox.Enabled)
        {
            // Register IInboxStore (EF Core implementation)
            services.AddScoped<IInboxStore, InboxStore<TDbContext>>();

            // Register IInboxProcessor
            services.AddScoped<IInboxProcessor, InboxProcessor>();

            // Register IConsumerIdProvider (default implementation)
            services.AddSingleton<IConsumerIdProvider, ConsumerIdProvider>();

            // Register inbox retention cleanup service
            services.AddSingleton<IHostedService, InboxRetentionCleanupService<TDbContext>>();
        }

        // Store entity name map in service collection for use by filters and formatters
        services.AddSingleton(entityNameMap);

        // Register MassTransit with typed outbox support
        RegisterMassTransitWithOutbox<TDbContext>(
            services,
            configuration,
            serviceName,
            configureConsumers,
            configure,
            options,
            entityNameMap);

        return services;
    }

    /// <summary>
    /// Adds MassTransit messaging infrastructure without compile-time DbContext.
    /// This overload throws an exception if Outbox or Inbox are enabled (they require DbContext type).
    /// Use AddMessaging&lt;TDbContext&gt;() instead if you need Outbox/Inbox features.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration root</param>
    /// <param name="serviceName">Service name used in endpoint naming</param>
    /// <param name="configureConsumers">Action to register MassTransit consumers</param>
    /// <param name="configure">Optional action to configure entity name mappings</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure = null)
    {
        // Build entity name map and get DbContext type from builder
        var builder = new MessagingBuilder(configuration);
        configure?.Invoke(builder);
        var serviceDbContextType = builder.GetServiceDbContextType();

        // Bind and validate options
        var messagingSection = configuration.GetSection(MessagingOptions.SectionName);
        services.Configure<MessagingOptions>(messagingSection);
        services.AddOptions<MessagingOptions>().Bind(messagingSection).ValidateOnStart();

        var options = new MessagingOptions();
        messagingSection.Bind(options);

        // Validate that if Outbox/Inbox are enabled, DbContext type is provided
        if (options.Outbox.Enabled || options.Inbox.Enabled)
        {
            if (serviceDbContextType == null)
            {
                throw new InvalidOperationException(
                    "Outbox or Inbox is enabled but no DbContext type is specified. " +
                    "Use AddMessaging<TDbContext>(...) overload instead, or call builder.WithServiceDbContext<T>() in the configure action.");
            }
        }

        // If DbContext type is provided, delegate to typed overload
        if (serviceDbContextType != null)
        {
            // Use reflection to call the generic overload
            var method = typeof(MessagingServiceCollectionExtensions)
                .GetMethod(nameof(AddMessaging), new[] { typeof(IServiceCollection), typeof(IConfiguration), typeof(string), typeof(Action<IBusRegistrationConfigurator>), typeof(Action<MessagingBuilder>) })
                ?.MakeGenericMethod(serviceDbContextType)
                ?? throw new InvalidOperationException($"Failed to find AddMessaging<TDbContext> method");

            return (IServiceCollection)method.Invoke(null, [services, configuration, serviceName, configureConsumers, configure])!;
        }

        // No DbContext needed - use internal method without outbox/inbox
        return AddMessagingInternal(services, configuration, serviceName, configureConsumers, configure, null);
    }

    private static IServiceCollection AddMessagingInternal(
        IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure,
        Type? dbContextType)
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

        // Build entity name map
        var builder = new MessagingBuilder(configuration);
        configure?.Invoke(builder);
        var entityNameMap = builder.BuildEntityNameMap();

        // Validate DbContext requirements
        if (options.Outbox.Enabled && dbContextType == null)
        {
            throw new InvalidOperationException(
                "Outbox is enabled but no service DbContext type is specified. " +
                "Use AddMessaging<TDbContext>(...) overload or call builder.WithServiceDbContext<T>() in the configure action.");
        }

        if (options.Inbox.Enabled && dbContextType == null)
        {
            throw new InvalidOperationException(
                "Inbox is enabled but no service DbContext type is specified. " +
                "Use AddMessaging<TDbContext>(...) overload or call builder.WithServiceDbContext<T>() in the configure action.");
        }

        // Register inbox services if enabled (using reflection since we have Type at runtime)
        if (options.Inbox.Enabled && dbContextType != null)
        {
            // Register IInboxStore (EF Core implementation)
            var storeType = typeof(InboxStore<>).MakeGenericType(dbContextType);
            services.AddScoped(typeof(IInboxStore), storeType);

            // Register IInboxProcessor
            services.AddScoped<IInboxProcessor, InboxProcessor>();

            // Register IConsumerIdProvider (default implementation)
            services.AddSingleton<IConsumerIdProvider, ConsumerIdProvider>();

            // Register inbox retention cleanup service
            var serviceType = typeof(InboxRetentionCleanupService<>).MakeGenericType(dbContextType);
            services.Add(ServiceDescriptor.Singleton(typeof(IHostedService), serviceType));
        }

        // Store entity name map in service collection for use by filters and formatters
        services.AddSingleton(entityNameMap);

        // Register MassTransit
        services.AddMassTransit(x =>
        {
            // Add EF Core outbox if enabled (requires compile-time generic, so we handle it in typed overload)
            // For non-typed path, outbox registration is skipped (validation above ensures error is thrown)

            // Add delayed message scheduler if enabled
            if (options.Scheduler.Enabled)
            {
                x.AddDelayedMessageScheduler();
            }

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

                // Use delayed message scheduler if enabled
                if (messagingOptions.Scheduler.Enabled)
                {
                    cfg.UseDelayedMessageScheduler();
                }

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

                // Configure inbox filter if enabled
                if (messagingOptions.Inbox.Enabled && dbContextType != null)
                {
                    // Register inbox filter - it will use IInboxProcessor which uses IInboxStore
                    cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);
                }

                // Configure endpoints with endpoint name formatter
                // NOTE: ConfigureEndpoints filter parameter (e => { ... }) receives RegistrationFilterConfigurator,
                // NOT IReceiveEndpointConfigurator. It cannot be used for endpoint middleware like UseEntityFrameworkOutbox.
                // Outbox must be configured via x.AddEntityFrameworkOutbox<TDbContext>() in AddMassTransit registration.
                var endpointFormatter = new KebabCaseEndpointNameFormatter(
                    serviceName,
                    false,
                    entityNameFormatter);

                cfg.ConfigureEndpoints(context, endpointFormatter);
            });
        });

        return services;
    }

    /// <summary>
    /// Internal helper that registers MassTransit with EF Core outbox support.
    /// This is called from the typed AddMessaging&lt;TDbContext&gt; overload.
    /// </summary>
    private static void RegisterMassTransitWithOutbox<TDbContext>(
        IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure,
        MessagingOptions options,
        Dictionary<Type, string> entityNameMap)
        where TDbContext : DbContext
    {
        services.AddMassTransit(x =>
        {
            // Add EF Core outbox if enabled (this is the correct MassTransit way)
            if (options.Outbox.Enabled)
            {
                x.AddEntityFrameworkOutbox<TDbContext>(o =>
                {
                    o.QueryDelay = TimeSpan.FromSeconds(options.Outbox.QueryDelaySeconds);
                    // Note: DeliveryLimit is not available in IEntityFrameworkOutboxConfigurator
                    // It's handled by the outbox delivery service internally
                });
            }

            // Add delayed message scheduler if enabled
            if (options.Scheduler.Enabled)
            {
                x.AddDelayedMessageScheduler();
            }

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

                // Use delayed message scheduler if enabled
                if (messagingOptions.Scheduler.Enabled)
                {
                    cfg.UseDelayedMessageScheduler();
                }

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

                // Configure inbox filter if enabled
                if (messagingOptions.Inbox.Enabled)
                {
                    // Register inbox filter - it will use IInboxProcessor which uses IInboxStore
                    cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);
                }

                // Configure endpoints with endpoint name formatter
                // NOTE: ConfigureEndpoints filter parameter (e => { ... }) receives RegistrationFilterConfigurator,
                // NOT IReceiveEndpointConfigurator. It cannot be used for endpoint middleware like UseEntityFrameworkOutbox.
                // Outbox must be configured via x.AddEntityFrameworkOutbox<TDbContext>() in AddMassTransit registration.
                var endpointFormatter = new KebabCaseEndpointNameFormatter(
                    serviceName,
                    false,
                    entityNameFormatter);

                cfg.ConfigureEndpoints(context, endpointFormatter);
            });
        });
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
}
