using MassTransit;

namespace Combats.Infrastructure.Messaging.Naming;

public class EntityNameConvention : IEntityNameFormatter
{
    private readonly Dictionary<Type, string> _entityNameMap;
    private readonly string _entityNamePrefix;
    private readonly bool _useKebabCase;

    public EntityNameConvention(Dictionary<Type, string>? entityNameMap = null, string entityNamePrefix = "combats", bool useKebabCase = true)
    {
        _entityNameMap = entityNameMap ?? new Dictionary<Type, string>();
        _entityNamePrefix = entityNamePrefix;
        _useKebabCase = useKebabCase;
    }

    public string FormatEntityName<T>()
    {
        return FormatEntityName(typeof(T));
    }

    public string FormatEntityName(Type messageType)
    {
        if (_entityNameMap.TryGetValue(messageType, out var mappedName))
        {
            return mappedName;
        }

        var typeName = messageType.Name;
        var entityName = _useKebabCase ? EntityNameFormatter.ToKebabCase(typeName) : typeName;

        if (!string.IsNullOrWhiteSpace(_entityNamePrefix))
        {
            entityName = $"{_entityNamePrefix}.{entityName}";
        }

        return entityName;
    }
}



