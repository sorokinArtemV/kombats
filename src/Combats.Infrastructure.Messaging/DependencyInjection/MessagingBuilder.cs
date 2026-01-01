using MassTransit;
using Microsoft.EntityFrameworkCore;
using Combats.Infrastructure.Messaging.Naming;

namespace Combats.Infrastructure.Messaging.DependencyInjection;

public class MessagingBuilder
{
    private readonly Dictionary<Type, string> _entityNameMap = new();
    private Type? _serviceDbContextType;

    internal MessagingBuilder()
    {
    }

    public MessagingBuilder MapEntityName<T>(string entityName)
        where T : class
    {
        _entityNameMap[typeof(T)] = entityName;
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

    internal Dictionary<Type, string> GetEntityNameMap() => _entityNameMap;
    internal Type? GetServiceDbContextType() => _serviceDbContextType;
}

