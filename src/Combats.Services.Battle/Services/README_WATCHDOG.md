# Battle Watchdog Service

## Overview

The `BattleWatchdogService` is a background service that prevents battles from stalling if `ScheduleSend` fails due to transient RabbitMQ issues.

## Anti-Stall Mechanism (Option A - Preferred)

The watchdog implements a **recovery mechanism** that:

1. **Scans active battles** every 5 seconds (`ScanIntervalSeconds`)
2. **Checks battles in TurnOpen phase** for missing or overdue `ResolveTurn` schedules
3. **Reschedules missing/overdue ResolveTurn** commands if:
   - `NextResolveScheduledUtcTicks` is not set (never scheduled)
   - Deadline has passed with a 2-second grace period AND scheduled time is stale

## How It Works

- **State Tracking**: Every time `ResolveTurn` is scheduled, `NextResolveScheduledUtcTicks` is stored in Redis via `MarkResolveScheduledAsync`
- **Recovery**: If a battle is in `TurnOpen` phase but has no scheduled `ResolveTurn` (or it's overdue), the watchdog reschedules it
- **Idempotency**: The watchdog only reschedules if needed, preventing duplicate resolves

## Registration

Register the watchdog service in your DI container (e.g., `Program.cs` or `Startup.cs`):

```csharp
services.AddHostedService<BattleWatchdogService>();
```

## Configuration

- `ScanIntervalSeconds = 5`: How often to scan for missing schedules
- `GracePeriodSeconds = 2`: Grace period before considering a schedule overdue

## Notes

- The watchdog uses `IMessageScheduler.ScheduleSend` which doesn't support pipe configuration for explicit `MessageId`/`CorrelationId`
- MassTransit auto-generates `MessageId`; `CorrelationId` should be `battleId` for observability
- The watchdog is idempotent and won't create duplicate resolves



