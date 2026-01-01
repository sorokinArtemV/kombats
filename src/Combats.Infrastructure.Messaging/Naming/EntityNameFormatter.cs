using System.Text;

namespace Combats.Infrastructure.Messaging.Naming;

public static class EntityNameFormatter
{
    public static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder();
        var previousWasUpper = false;

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && !previousWasUpper)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
                previousWasUpper = true;
            }
            else
            {
                sb.Append(c);
                previousWasUpper = false;
            }
        }

        return sb.ToString();
    }

    public static string FormatQueueName(string serviceName, string endpoint)
    {
        var service = ToKebabCase(serviceName);
        var endpointName = ToKebabCase(endpoint);
        return $"{service}.{endpointName}";
    }

    public static string FormatEntityName(string entityName)
    {
        return ToKebabCase(entityName);
    }
}



