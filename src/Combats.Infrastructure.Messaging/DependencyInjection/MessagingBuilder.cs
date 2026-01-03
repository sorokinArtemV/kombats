using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Combats.Infrastructure.Messaging.Naming;

namespace Combats.Infrastructure.Messaging.DependencyInjection;

public class MessagingBuilder
{
    private readonly Dictionary<Type, string> _logicalKeyMap = new(); // Type -> logical key
    private readonly IConfiguration _configuration;
    private Type? _serviceDbContextType;

    internal MessagingBuilder(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Registers a logical key for a message type. The logical key is used to look up
    /// the actual entity name from configuration (Messaging:Topology:EntityNameMappings).
    /// </summary>
    /// <typeparam name="T">Message type (command or event)</typeparam>
    /// <param name="logicalKey">Logical key (e.g., "CreateBattle", "BattleCreated")</param>
    /// <returns>Builder for method chaining</returns>
    public MessagingBuilder Map<T>(string logicalKey)
        where T : class
    {
        _logicalKeyMap[typeof(T)] = logicalKey;
        return this;
    }

    /// <summary>
    /// Directly maps a message type to an entity name (bypasses configuration lookup).
    /// Use this only if you don't want to use configuration-based mapping.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="entityName">Entity name (e.g., "battle.create-battle")</param>
    /// <returns>Builder for method chaining</returns>
    public MessagingBuilder MapEntityName<T>(string entityName)
        where T : class
    {
        _logicalKeyMap[typeof(T)] = entityName; // Store as both logical key and entity name
        return this;
    }

    public MessagingBuilder WithServiceDbContext<TDbContext>()
        where TDbContext : DbContext
    {
        _serviceDbContextType = typeof(TDbContext);
        return this;
    }

    public MessagingBuilder WithOutbox<TDbContext>()
        where TDbContext : DbContext
    {
        _serviceDbContextType = typeof(TDbContext);
        return this;
    }

    public MessagingBuilder WithInbox<TDbContext>()
        where TDbContext : DbContext
    {
        _serviceDbContextType = typeof(TDbContext);
        return this;
    }

    /// <summary>
    /// Builds the entity name map by resolving logical keys from configuration.
    /// Throws InvalidOperationException if a registered type doesn't have a mapping in configuration.
    /// </summary>
    internal Dictionary<Type, string> BuildEntityNameMap()
    {
        var entityNameMap = new Dictionary<Type, string>();
        var mappingsSection = _configuration.GetSection("Messaging:Topology:EntityNameMappings");
        var mappings = mappingsSection.Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

        foreach (var (messageType, logicalKeyOrEntityName) in _logicalKeyMap)
        {
            // Check if it's a direct entity name (contains dot, indicating it's already a full entity name like "battle.create-battle")
            if (logicalKeyOrEntityName.Contains('.'))
            {
                // Direct entity name mapping (bypasses configuration)
                entityNameMap[messageType] = logicalKeyOrEntityName;
            }
            else
            {
                // Logical key - resolve from configuration
                if (!mappings.TryGetValue(logicalKeyOrEntityName, out var entityName))
                {
                    throw new InvalidOperationException(
                        $"Entity name mapping not found for logical key '{logicalKeyOrEntityName}' (message type: {messageType.Name}). " +
                        $"Add it to configuration section 'Messaging:Topology:EntityNameMappings'.");
                }

                if (string.IsNullOrWhiteSpace(entityName))
                {
                    throw new InvalidOperationException(
                        $"Entity name mapping for logical key '{logicalKeyOrEntityName}' (message type: {messageType.Name}) is empty. " +
                        $"Check configuration section 'Messaging:Topology:EntityNameMappings'.");
                }

                entityNameMap[messageType] = entityName;
            }
        }

        return entityNameMap;
    }

    internal Type? GetServiceDbContextType() => _serviceDbContextType;
}
