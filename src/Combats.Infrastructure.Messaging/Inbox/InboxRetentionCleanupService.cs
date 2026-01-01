using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Combats.Infrastructure.Messaging.Options;

namespace Combats.Infrastructure.Messaging.Inbox;

public abstract class InboxRetentionCleanupServiceBase : BackgroundService
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IOptions<MessagingOptions> Options;
    protected readonly ILogger Logger;
    protected readonly TimeSpan CleanupInterval;

    protected InboxRetentionCleanupServiceBase(
        IServiceProvider serviceProvider,
        IOptions<MessagingOptions> options,
        ILogger logger)
    {
        ServiceProvider = serviceProvider;
        Options = options;
        Logger = logger;
        CleanupInterval = TimeSpan.FromMinutes(options.Value.Inbox.CleanupIntervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Value.Inbox.Enabled)
        {
            Logger.LogInformation("Inbox is disabled, retention cleanup service will not run");
            return;
        }

        Logger.LogInformation(
            "Inbox retention cleanup service started, interval: {Interval} minutes",
            Options.Value.Inbox.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during inbox retention cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    protected abstract Task CleanupExpiredMessagesAsync(CancellationToken cancellationToken);
}

public class InboxRetentionCleanupService<TDbContext> : InboxRetentionCleanupServiceBase
    where TDbContext : DbContext
{
    public InboxRetentionCleanupService(
        IServiceProvider serviceProvider,
        IOptions<MessagingOptions> options,
        ILogger<InboxRetentionCleanupService<TDbContext>> logger)
        : base(serviceProvider, options, logger)
    {
    }

    protected override async Task CleanupExpiredMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = ServiceProvider.CreateScope();
        var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        var cutoff = DateTime.UtcNow;
        var deletedCount = await inboxStore.DeleteExpiredAsync(cutoff, cancellationToken);

        if (deletedCount > 0)
        {
            Logger.LogInformation(
                "Deleted {Count} expired inbox messages (expired before {Cutoff})",
                deletedCount, cutoff);
        }
    }
}

