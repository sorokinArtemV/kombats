using MassTransit;

namespace Combats.Infrastructure.Messaging.Naming;

public class KebabCaseEndpointNameFormatter : IEndpointNameFormatter
{
    private readonly string _serviceName;
    private readonly bool _includeNamespace;
    private readonly EntityNameConvention? _entityNameFormatter;

    public KebabCaseEndpointNameFormatter(
        string serviceName, 
        bool includeNamespace = false,
        EntityNameConvention? entityNameFormatter = null)
    {
        _serviceName = serviceName;
        _includeNamespace = includeNamespace;
        _entityNameFormatter = entityNameFormatter;
    }

    public string Separator => ".";

    public string Consumer<T>() where T : class, IConsumer
    {
        var typeName = typeof(T).Name;
        if (typeName.EndsWith("Consumer"))
            typeName = typeName.Substring(0, typeName.Length - 8);

        return EntityNameFormatter.FormatQueueName(_serviceName, typeName);
    }

    public string Message<T>() where T : class
    {
        // Use entity name formatter if available to respect canonical entity names
        if (_entityNameFormatter != null)
        {
            return _entityNameFormatter.FormatEntityName<T>();
        }
        
        return EntityNameFormatter.FormatEntityName(typeof(T).Name);
    }

    public string ExecuteActivity<T, TArguments>() where T : class, IExecuteActivity<TArguments> where TArguments : class
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, typeof(T).Name);
    }

    public string CompensateActivity<T, TLog>() where T : class, ICompensateActivity<TLog> where TLog : class
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, typeof(T).Name);
    }

    public string Saga<T>() where T : class, ISaga
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, typeof(T).Name);
    }

    public string ExecuteActivity(string name)
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, name);
    }

    public string CompensateActivity(string name)
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, name);
    }

    public string SanitizeName(string name)
    {
        return EntityNameFormatter.ToKebabCase(name);
    }

    public string TemporaryEndpoint(string tag)
    {
        return EntityNameFormatter.FormatQueueName(_serviceName, $"temp-{tag}");
    }
}