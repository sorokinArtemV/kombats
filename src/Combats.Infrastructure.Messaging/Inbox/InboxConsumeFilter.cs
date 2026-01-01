using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Combats.Infrastructure.Messaging.Options;

namespace Combats.Infrastructure.Messaging.Inbox;

public class InboxConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    private readonly IInboxProcessor _inboxProcessor;
    private readonly IConsumerIdProvider _consumerIdProvider;
    private readonly IOptions<MessagingOptions> _options;
    private readonly ILogger<InboxConsumeFilter<T>> _logger;

    public InboxConsumeFilter(
        IInboxProcessor inboxProcessor,
        IConsumerIdProvider consumerIdProvider,
        IOptions<MessagingOptions> options,
        ILogger<InboxConsumeFilter<T>> logger)
    {
        _inboxProcessor = inboxProcessor;
        _consumerIdProvider = consumerIdProvider;
        _options = options;
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (!_options.Value.Inbox.Enabled)
        {
            await next.Send(context);
            return;
        }

        var consumerId = _consumerIdProvider.GetConsumerId(context);

        await _inboxProcessor.ProcessAsync(
            context,
            consumerId,
            next.Send,
            context.CancellationToken);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("inboxIdempotency");
    }
}

