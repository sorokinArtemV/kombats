# Аудит архитектуры Battle Engine

**Дата аудита:** 2025-01-XX  
**Аудитор:** Senior Software Architect  
**Область:** Distributed systems, realtime game backend на .NET

---

## 1. Inventory

### 1.1 Карта проектов/папок

```
Kombats/
├── src/
│   ├── Combats.Contracts/          # Message contracts (команды/события)
│   │   └── Battle/
│   ├── Combats.Infrastructure.Messaging/  # Инфраструктура messaging (outbox/inbox)
│   │   ├── DependencyInjection/
│   │   ├── Filters/
│   │   ├── Inbox/
│   │   ├── Naming/
│   │   └── Options/
│   └── Combats.Services.Battle/    # Battle/Game Engine сервис
│       ├── Consumers/
│       ├── Data/                    # EF Core + Postgres
│       ├── DTOs/                   # SignalR DTOs
│       ├── Hubs/                   # SignalR hubs
│       ├── Services/               # Background services
│       └── State/                  # Redis state store
└── docs/
```

**Отсутствующие сервисы:**
- `Combats.Services.Matchmaking` - **NOT FOUND** (упоминается в README, но кода нет)
- `Combats.Services.Auth` - **NOT FOUND**
- `Combats.Services.Gateway` - **NOT FOUND**

### 1.2 Точки входа

#### Consumers (MassTransit)
- `CreateBattleConsumer` → `src/Combats.Services.Battle/Consumers/CreateBattleConsumer.cs`
  - Queue: `battle.create-battle` (kebab-case, serviceName.endpointName)
- `BattleCreatedEngineConsumer` → `src/Combats.Services.Battle/Consumers/BattleCreatedEngineConsumer.cs`
  - Queue: `battle.battle-created` (event subscription)
- `ResolveTurnConsumer` → `src/Combats.Services.Battle/Consumers/ResolveTurnConsumer.cs`
  - Queue: `battle.resolve-turn` (см. `BattleQueues.ResolveTurn`)
- `EndBattleConsumer` → `src/Combats.Services.Battle/Consumers/EndBattleConsumer.cs`
  - Queue: `battle.end-battle`

#### Hosted Services
- `BattleWatchdogService` → `src/Combats.Services.Battle/Services/BattleWatchdogService.cs`
  - **ПРОБЛЕМА:** Не зарегистрирован в `Program.cs` (см. раздел 9)
- `InboxRetentionCleanupService<TDbContext>` → `src/Combats.Infrastructure.Messaging/Inbox/InboxRetentionCleanupService.cs`
  - Автоматически регистрируется при включенном inbox

#### SignalR Hubs
- `BattleHub` → `src/Combats.Services.Battle/Hubs/BattleHub.cs`
  - Endpoints:
    - `JoinBattle(Guid battleId)` → возвращает `BattleSnapshotDto`
    - `SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)`
  - Группы: `battle:{battleId}`

#### HTTP Endpoints
- **NOT FOUND** - нет явных HTTP контроллеров (только SignalR)

### 1.3 Message Contracts

#### Commands (Send, point-to-point)
| Contract | File | Queue/Exchange | Routing Key |
|----------|-----|------------------|-------------|
| `CreateBattle` | `src/Combats.Contracts/Battle/CreateBattle.cs` | `battle.create-battle` | N/A (queue) |
| `ResolveTurn` | `src/Combats.Contracts/Battle/ResolveTurn.cs` | `battle.resolve-turn` | N/A (queue) |
| `EndBattle` | `src/Combats.Contracts/Battle/EndBattle.cs` | `battle.end-battle` | N/A (queue) |

#### Events (Publish, pub/sub)
| Contract | File | Exchange | Routing Key |
|----------|-----|----------|-------------|
| `BattleCreated` | `src/Combats.Contracts/Battle/BattleCreated.cs` | `battle.battle-created` | Auto (MassTransit) |
| `BattleEnded` | `src/Combats.Contracts/Battle/BattleEnded.cs` | `battle.battle-ended` | Auto (MassTransit) |

**Структура контрактов:**
- Все используют `record` с `init`-only properties
- `CreateBattle` включает вложенный `Ruleset` record
- `BattleCreated` и `BattleEnded` включают `MatchId`, `Version`

### 1.4 Хранилища

#### Postgres (EF Core, BattleDbContext)

**Таблицы:**
1. `battles` → `src/Combats.Services.Battle/Data/Entities/BattleEntity.cs`
   - PK: `BattleId` (Guid, не автоинкремент)
   - Колонки: `MatchId`, `PlayerAId`, `PlayerBId`, `State` (string), `CreatedAt`, `EndedAt`, `EndReason`, `WinnerPlayerId`
   - Index: `MatchId`
   - Migration: `20250101000000_InitialCreate.cs`

2. `inbox_messages` → `src/Combats.Infrastructure.Messaging/Inbox/InboxMessage.cs`
   - PK: composite `(message_id, consumer_id)`
   - Колонки: `status` (enum → int), `received_at`, `processed_at`, `expires_at`
   - Indexes: `expires_at`, `status`, `consumer_id`
   - Migration: `20250101000001_InboxMessages.cs`
   - TTL: `expires_at` (retention 7 дней по умолчанию)

3. **Outbox таблицы (MassTransit):**
   - **NOT FOUND в миграциях** - MassTransit создает автоматически через `UseEntityFrameworkOutbox<T>`
   - Ожидаемые таблицы: `OutboxMessage`, `OutboxState` (создаются при первом запуске или через миграцию)

#### Redis (StackExchange.Redis)

**Паттерны ключей:**
- `battle:state:{battleId}` → JSON `BattleState` (без TTL, удаляется вручную при Ended)
- `battle:action:{battleId}:turn:{turnIndex}:player:{playerId}` → string action payload (TTL: 1 час)
- `battle:active` → Redis Set (GUID строки активных боев, без TTL)

**Структура `BattleState` (JSON в Redis):**
```json
{
  "BattleId": "guid",
  "MatchId": "guid",
  "PlayerAId": "guid",
  "PlayerBId": "guid",
  "Ruleset": { "Version": 1, "TurnSeconds": 10, "NoActionLimit": 3, "Seed": 123 },
  "Phase": 0-3,  // 0=ArenaOpen, 1=TurnOpen, 2=Resolving, 3=Ended
  "TurnIndex": 1,
  "DeadlineUtcTicks": 638123456789012345,
  "NextResolveScheduledUtcTicks": 638123456789012345,
  "NoActionStreakBoth": 0,
  "LastResolvedTurnIndex": 0,
  "Version": 1
}
```

**Код:** `src/Combats.Services.Battle/State/RedisBattleStateStore.cs`

---

## 2. Architecture & Dataflow

### 2.A Matchmaking → Create Battle → Notify Players

**ТЕКУЩИЙ СТАТУС:** Matchmaking сервис **NOT FOUND** в коде.

**Ожидаемый flow (Variant A):**
1. Matchmaking (внешний) отправляет команду `CreateBattle` → Queue: `battle.create-battle`
2. `CreateBattleConsumer` (`src/Combats.Services.Battle/Consumers/CreateBattleConsumer.cs:23-90`):
   - Создает `BattleEntity` в Postgres (`State = "ArenaOpen"`)
   - `SaveChangesAsync()` (транзакция)
   - Публикует `BattleCreated` через `context.Publish()` (outbox)
   - `SaveChangesAsync()` снова (для outbox)
3. `BattleCreatedEngineConsumer` (`src/Combats.Services.Battle/Consumers/BattleCreatedEngineConsumer.cs:30-139`):
   - Инициализирует `BattleState` в Redis (`SETNX`, idempotent)
   - Открывает Turn 1 (`TryOpenTurnAsync`)
   - Уведомляет SignalR: `BattleReady` + `TurnOpened`
   - Планирует `ResolveTurn(battleId, 1)` через `IMessageScheduler.ScheduleSend` на deadline
   - Сохраняет `NextResolveScheduledUtcTicks` в Redis

**Проблемы:**
- Matchmaking не реализован → нет полного flow
- Outbox использует `UseInMemoryOutbox` в `ServiceCollectionExtensions.cs:58`, но должен быть `UseEntityFrameworkOutbox<T>` (см. раздел 4)

### 2.B Turn Lifecycle (TurnOpen → Resolving)

**Flow:**
1. **TurnOpen phase:**
   - Клиент подключается: `BattleHub.JoinBattle()` → получает `BattleSnapshotDto`
   - Клиент отправляет действие: `BattleHub.SubmitTurnAction()` → валидация → сохранение в Redis
   - Дедлайн: хранится в `BattleState.DeadlineUtcTicks`

2. **ResolveTurn scheduled message arrives:**
   - `ResolveTurnConsumer` (`src/Combats.Services.Battle/Consumers/ResolveTurnConsumer.cs:31-287`):
     - Проверка idempotency: `turnIndex <= LastResolvedTurnIndex` → ACK
     - Валидация: `Phase == TurnOpen && TurnIndex == message.TurnIndex`
     - Атомарный переход: `TryMarkTurnResolvingAsync` (Lua script)
     - Чтение действий: `GetActionsAsync(battleId, turnIndex, ...)`
     - Логика: если оба `NoAction` → `NoActionStreakBoth++`, иначе сброс
     - Если `NoActionStreakBoth >= NoActionLimit`:
       - `EndBattleAndMarkResolvedAsync` (атомарно)
       - Публикация `BattleEnded` (DoubleForfeit)
       - SignalR: `BattleEnded`
     - Иначе:
       - `MarkTurnResolvedAndOpenNextAsync` (атомарно: `LastResolvedTurnIndex++`, `TurnIndex++`, `Phase=TurnOpen`)
       - SignalR: `TurnOpened` (следующий ход)
       - Планирование `ResolveTurn(nextTurnIndex)` на новый deadline
       - Сохранение `NextResolveScheduledUtcTicks`

3. **Watchdog recovery (если ScheduleSend failed):**
   - `BattleWatchdogService` (`src/Combats.Services.Battle/Services/BattleWatchdogService.cs:60-143`):
     - Сканирует `battle:active` каждые 5 секунд
     - Для `Phase == TurnOpen`: проверяет `NextResolveScheduledUtcTicks`
     - Если missing/overdue → reschedules `ResolveTurn`

**Код переходов:**
- `TryOpenTurnAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:86-135` (Lua script)
- `TryMarkTurnResolvingAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:137-174` (Lua script)
- `MarkTurnResolvedAndOpenNextAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:176-227` (Lua script)

### 2.C Reconnect Flow

**Flow:**
1. Клиент переподключается к SignalR
2. Вызов `BattleHub.JoinBattle(battleId)` (`src/Combats.Services.Battle/Hubs/BattleHub.cs:24-87`):
   - Проверка JWT: `ClaimTypes.NameIdentifier` или `"sub"` → `userId`
   - Добавление в группу: `Groups.AddToGroupAsync($"battle:{battleId}")`
   - Загрузка state: `GetStateAsync(battleId)` из Redis
   - Проверка участника: `PlayerAId == userId || PlayerBId == userId`
   - Возврат `BattleSnapshotDto` (snapshot, не event replay)

**Данные в snapshot:**
- `BattleId`, `PlayerAId`, `PlayerBId`, `Ruleset`
- `Phase` (string), `TurnIndex`, `DeadlineUtc` (ISO string)
- `NoActionStreakBoth`, `LastResolvedTurnIndex`, `Version`

**Риски:**
- Нет event replay → клиент не знает историю действий
- Нет ограничения на количество переподключений (rate limit отсутствует)

---

## 3. State Machines

### 3.1 Matchmaking Saga

**СТАТУС:** **NOT FOUND** - сервис Matchmaking отсутствует в коде.

**Ожидаемая таблица (по требованиям):**

| State | Trigger/Event | Action | Next State | Idempotency Strategy |
|-------|---------------|--------|------------|---------------------|
| Created | StartMatchmaking | Инициализация, добавление в очередь | BattleCreateRequested | SETNX в Redis |
| BattleCreateRequested | CreateBattle sent | Ожидание BattleCreated | BattleCreated | Проверка state |
| BattleCreated | BattleCreated received | Сохранение BattleId | Notified | Idempotent по BattleId |
| Notified | Players notified | Завершение | Completed | Проверка state |
| Completed | - | Финальное состояние | - | N/A |

**Инварианты (ожидаемые, но не реализованы):**
1. ❌ Нельзя перейти из `Completed` в любой другой state
2. ❌ Нельзя отправить `CreateBattle` дважды для одного match
3. ❌ Нельзя перейти в `Notified` без получения `BattleCreated`
4. ❌ Нельзя завершить saga без уведомления игроков
5. ❌ Нельзя создать battle без валидного matchId

### 3.2 Battle State Machine

**Реализация:** `src/Combats.Services.Battle/State/BattlePhase.cs`

| State | Trigger | Guard Conditions | Action | Next State |
|-------|---------|------------------|--------|------------|
| ArenaOpen (0) | `BattleCreated` event | `LastResolvedTurnIndex == 0` | `TryOpenTurnAsync(1)` | TurnOpen (1) |
| TurnOpen (1) | `ResolveTurn` command | `Phase == TurnOpen && TurnIndex == message.TurnIndex` | `TryMarkTurnResolvingAsync` | Resolving (2) |
| Resolving (2) | Actions processed | `Phase == Resolving && TurnIndex == currentTurn` | `MarkTurnResolvedAndOpenNextAsync` или `EndBattleAndMarkResolvedAsync` | TurnOpen (1) или Ended (3) |
| Ended (3) | DoubleForfeit или EndBattle | `Phase == Resolving` | `EndBattleAndMarkResolvedAsync` | Ended (3) |

**Код переходов:**
- `TryOpenTurnAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:86-135`
- `TryMarkTurnResolvingAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:137-174`
- `MarkTurnResolvedAndOpenNextAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:176-227`
- `EndBattleAndMarkResolvedAsync`: `src/Combats.Services.Battle/State/RedisBattleStateStore.cs:229-277`

**Инварианты (реализованы в Lua scripts):**

1. ✅ **Нельзя открыть turn N если `LastResolvedTurnIndex != N-1`**
   - Код: `RedisBattleStateStore.cs:104-106` (Lua script)
2. ✅ **Нельзя открыть turn если `Phase == Ended`**
   - Код: `RedisBattleStateStore.cs:99-101` (Lua script)
3. ✅ **Нельзя открыть turn если `Phase != ArenaOpen && Phase != Resolving`**
   - Код: `RedisBattleStateStore.cs:108-111` (Lua script)
4. ✅ **Нельзя перейти в Resolving если `Phase != TurnOpen`**
   - Код: `RedisBattleStateStore.cs:149-152` (Lua script)
5. ✅ **Нельзя разрешить turn если `Phase != Resolving`**
   - Код: `RedisBattleStateStore.cs:195-198` (Lua script)
6. ✅ **Нельзя завершить battle дважды (idempotent)**
   - Код: `RedisBattleStateStore.cs:243-246` (Lua script)
7. ✅ **Нельзя завершить battle если `Phase != Resolving`**
   - Код: `RedisBattleStateStore.cs:247-250` (Lua script)
8. ✅ **Нельзя открыть следующий turn пока текущий не разрешен**
   - Код: `RedisBattleStateStore.cs:199-200` (устанавливает `LastResolvedTurnIndex`)

**Дополнительные инварианты (в C# коде):**

9. ✅ **Idempotency: `turnIndex <= LastResolvedTurnIndex` → ACK**
   - Код: `ResolveTurnConsumer.cs:64-70`
10. ✅ **Валидация: `Phase == TurnOpen && TurnIndex == message.TurnIndex`**
   - Код: `ResolveTurnConsumer.cs:73-98`

---

## 4. Reliability: Outbox/Inbox/Delivery

### 4.1 Transactional Outbox

**ПРОБЛЕМА:** Несоответствие конфигурации.

**Текущая конфигурация:**
- `ServiceCollectionExtensions.cs:58`: `cfg.UseInMemoryOutbox(context)` - **НЕПРАВИЛЬНО**
- `MessagingServiceCollectionExtensions.cs:195`: `endpoint.UseEntityFrameworkOutbox<TDbContext>(context)` - **ПРАВИЛЬНО**

**Где используется:**
- `CreateBattleConsumer.cs:63`: `await context.Publish(battleCreated, context.CancellationToken);` → затем `SaveChangesAsync()` (строка 66)
- `EndBattleConsumer.cs:76`: `await context.Publish(battleEnded, context.CancellationToken);` → затем `SaveChangesAsync()` (строка 79)
- `ResolveTurnConsumer.cs:187`: `await context.Publish(battleEnded, context.CancellationToken);` → **НЕТ SaveChangesAsync()** - **ПРОБЛЕМА**

**Проблемы:**
1. ❌ `CreateBattleConsumer` и `EndBattleConsumer` делают два `SaveChangesAsync()` - это не атомарно
2. ❌ `ResolveTurnConsumer` публикует `BattleEnded` без `SaveChangesAsync()` - outbox не сработает
3. ❌ `UseInMemoryOutbox` в `ServiceCollectionExtensions.cs` не используется (старый код?)
4. ⚠️ Outbox таблицы не созданы в миграциях (MassTransit создает автоматически, но лучше явно)

**Правильный паттерн:**
```csharp
// В одной транзакции:
_dbContext.Battles.Add(battle);
await context.Publish(event);  // Сохраняется в outbox
await _dbContext.SaveChangesAsync();  // Атомарно: battle + outbox
```

**Код проверки:**
- `CreateBattleConsumer.cs:41-66` - **НЕПРАВИЛЬНО** (два SaveChanges)
- `EndBattleConsumer.cs:61-79` - **НЕПРАВИЛЬНО** (два SaveChanges)
- `ResolveTurnConsumer.cs:187` - **НЕПРАВИЛЬНО** (нет SaveChanges, нет DbContext)

### 4.2 Inbox/Idempotency

**Реализация:** `src/Combats.Infrastructure.Messaging/Inbox/`

**Где хранится messageId:**
- Таблица: `inbox_messages` (PK: `(message_id, consumer_id)`)
- Код: `InboxStore.cs:26-29`

**Как дедуплицируется:**
1. `InboxConsumeFilter<T>` (`src/Combats.Infrastructure.Messaging/Inbox/InboxConsumeFilter.cs:28-42`):
   - Вызывает `InboxProcessor.ProcessAsync()`
2. `InboxProcessor` (`src/Combats.Infrastructure.Messaging/Inbox/InboxProcessor.cs:19-99`):
   - `TryBeginProcessingAsync(messageId, consumerId, expiresAt)`:
     - Проверка существования: `Status == Processed` → `AlreadyProcessed` → ACK
     - Проверка concurrent: `Status == Processing` → `CurrentlyProcessing` → throw retryable exception
     - Новое сообщение: insert как `Processing`
   - После успешного handler: `MarkProcessedAsync()` → `Status = Processed`
   - При ошибке: `ReleaseProcessingAsync()` → delete row (для retry)

**ConsumerId:**
- `ConsumerIdProvider` (`src/Combats.Infrastructure.Messaging/Inbox/ConsumerIdProvider.cs`):
  - Формат: `{ServiceName}.{ConsumerTypeName}` (например, `battle.CreateBattleConsumer`)

**Retry handling:**
- При `CurrentlyProcessing`: throw `InboxProcessingException` → MassTransit retry
- При ошибке handler: delete inbox row → MassTransit redelivery

**Код:**
- `InboxStore.cs:19-107` (TryBeginProcessingAsync)
- `InboxStore.cs:109-134` (MarkProcessedAsync)
- `InboxStore.cs:136-161` (ReleaseProcessingAsync)

### 4.3 Poison/DLQ Strategy

**Текущая реализация:**
- MassTransit retry: exponential (5 попыток, 200ms-5000ms)
  - Код: `MessagingServiceCollectionExtensions.cs:123-130`
- Redelivery: delayed (30s, 120s, 600s)
  - Код: `MessagingServiceCollectionExtensions.cs:133-142`
- Inbox: при ошибке удаляет row → позволяет retry

**Проблемы:**
1. ❌ **НЕТ явной DLQ конфигурации** - после всех retries сообщение теряется или застревает
2. ❌ **НЕТ мониторинга poison messages**
3. ⚠️ Inbox retention: 7 дней (можно настроить), cleanup каждые 15 минут

**Рекомендация:**
- Настроить DLQ exchange/queue в RabbitMQ
- Добавить метрики для poison messages

### 4.4 Риски

**Окна несогласованности:**
1. ⚠️ **Outbox:** между `Publish()` и `SaveChangesAsync()` - если процесс упадет, событие не сохранится
   - Код: `CreateBattleConsumer.cs:63-66` (два SaveChanges)
2. ⚠️ **Redis → Postgres:** `BattleState` в Redis, `BattleEntity` в Postgres - нет синхронизации
   - При падении Redis данные теряются (нет персистентности)
3. ⚠️ **SignalR → Redis:** действия сохраняются в Redis, но нет транзакции с state

**Duplicate processing:**
1. ✅ Защищено inbox (idempotency по `(messageId, consumerId)`)
2. ✅ Защищено в consumers (idempotency checks)
3. ⚠️ **SignalR actions:** нет idempotency для `SubmitTurnAction` - можно отправить дважды

**Ordering:**
1. ⚠️ **ResolveTurn:** scheduled messages могут прийти не по порядку (если deadline пропущен)
   - Защищено проверкой `turnIndex <= LastResolvedTurnIndex`
2. ⚠️ **BattleCreated:** может прийти дважды (at-least-once) - защищено `SETNX` в Redis

---

## 5. Protocol & Validation

### 5.1 Контракт действий игрока

**Текущая реализация:**
- `BattleHub.SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload)`
- `actionPayload`: `string` (JSON, валидируется парсингом)

**Схема (ожидаемая, но не документирована):**
```json
{
  "actionType": "attack|defend|special",
  "target": "playerA|playerB",
  "value": 100,
  "version": 1
}
```

**Проблемы:**
1. ❌ **НЕТ явной схемы/версии** - только JSON validation
2. ❌ **НЕТ документации** формата action payload
3. ⚠️ Валидация только JSON parsing (`BattleHub.cs:184-198`)

**Код валидации:**
- `BattleHub.cs:173-199`: проверка JSON через `JsonDocument.Parse()`
- При ошибке: `finalActionPayload = string.Empty` (NoAction)

### 5.2 Invalid Payload → NoAction Mapping

**Реализация:** `src/Combats.Services.Battle/Hubs/BattleHub.cs:90-207`

**Сценарии → NoAction:**
1. ✅ **Invalid phase:** `Phase != TurnOpen` → `StoreActionAsync(..., string.Empty)`
   - Код: `BattleHub.cs:142-150`
2. ✅ **TurnIndex mismatch:** `clientTurnIndex != serverTurnIndex` → NoAction для server turnIndex
   - Код: `BattleHub.cs:152-160`
3. ✅ **Deadline passed:** `UtcNow > deadlineUtc + 1s` → NoAction
   - Код: `BattleHub.cs:162-171`
4. ✅ **Invalid JSON:** `JsonException` → NoAction
   - Код: `BattleHub.cs:191-198`
5. ✅ **Empty payload:** `string.IsNullOrWhiteSpace` → NoAction
   - Код: `BattleHub.cs:175-181`

**Важно:** NoAction сохраняется для **текущего server turnIndex** (`state.TurnIndex`), не client-provided `turnIndex`.

### 5.3 Anti-Cheat/Anti-Abuse

**Реализовано:**
1. ✅ **JWT authentication:** `[Authorize]` на `BattleHub`
   - Код: `BattleHub.cs:10`
2. ✅ **Participant check:** `PlayerAId == userId || PlayerBId == userId`
   - Код: `BattleHub.cs:66-72`, `BattleHub.cs:129-135`
3. ✅ **Deadline validation:** действия после deadline → NoAction
   - Код: `BattleHub.cs:162-171`

**НЕ реализовано:**
1. ❌ **Rate limiting:** нет ограничения на количество `SubmitTurnAction` в секунду
2. ❌ **Join battle rate limit:** можно переподключаться бесконечно
3. ❌ **Action validation:** только JSON parsing, нет проверки значений (например, `value > 0`)
4. ❌ **Replay protection:** нет nonce/timestamp для предотвращения replay атак

**Рекомендации:**
- Добавить rate limiting middleware для SignalR
- Добавить action schema validation (JSON Schema или FluentValidation)
- Добавить nonce/timestamp в action payload

---

## 6. Timing & Determinism

### 6.1 Источник времени

**Реализация:**
- **Server clock:** `DateTime.UtcNow` используется везде
- Код:
  - `BattleCreatedEngineConsumer.cs:67`: `DateTime.UtcNow.AddSeconds(turnSeconds)`
  - `ResolveTurnConsumer.cs:102, 218`: `DateTime.UtcNow`
  - `BattleHub.cs:163`: `DateTime.UtcNow > deadlineUtc.AddSeconds(1)`

**Проблемы:**
1. ⚠️ **Clock drift:** нет синхронизации с NTP
2. ⚠️ **Clock skew:** при рестарте сервиса время может "прыгнуть"
3. ⚠️ **Deadline storage:** `DeadlineUtcTicks` (long) - нет явной timezone, но используется UTC

### 6.2 Таймеры ходов

**Реализация:**
- **Scheduling:** `IMessageScheduler.ScheduleSend()` (MassTransit delayed message scheduler)
- **Deadline calculation:**
  - Turn 1: `UtcNow + Ruleset.TurnSeconds` (`BattleCreatedEngineConsumer.cs:67`)
  - Next turns: `UtcNow + Ruleset.TurnSeconds` (drifting cadence) (`ResolveTurnConsumer.cs:218`)
- **Deadline storage:** `BattleState.DeadlineUtcTicks` (Redis)

**Cadence:**
- **Drifting:** `nextDeadline = UtcNow + turnSeconds` (не fixed cadence)
- Рациональное: см. комментарий в `ResolveTurnConsumer.cs:213-215`

**Код:**
- `BattleCreatedEngineConsumer.cs:115-119`: `ScheduleSend` для Turn 1
- `ResolveTurnConsumer.cs:263-267`: `ScheduleSend` для следующего turn

### 6.3 Избежание drift

**Текущая реализация:**
- Нет явной защиты от drift
- Watchdog может reschedule если deadline пропущен (`BattleWatchdogService.cs:91-98`)

**Проблемы:**
1. ⚠️ Если `ResolveTurn` приходит раньше deadline - все равно обрабатывается (логируется, но не блокируется)
   - Код: `ResolveTurnConsumer.cs:100-109` (только логирование)
2. ⚠️ Если resolution задерживается - следующий turn deadline сдвигается (drifting cadence)

### 6.4 Рестарт сервиса посреди TurnOpen

**Сценарий:**
1. Battle в `TurnOpen`, deadline через 5 секунд
2. Battle service рестартует
3. `ResolveTurn` уже запланирован в RabbitMQ (delayed message)
4. После рестарта:
   - Watchdog сканирует `battle:active` каждые 5 секунд
   - Если `NextResolveScheduledUtcTicks` не установлен или stale → reschedules
   - Если установлен и актуален → ничего не делает

**Защита:**
- ✅ Watchdog восстанавливает missing schedules
- ⚠️ Но если `ResolveTurn` уже в RabbitMQ, но `NextResolveScheduledUtcTicks` не сохранен → возможен duplicate

**Код:**
- `BattleWatchdogService.cs:60-143`: scan and recover logic

---

## 7. Reconnect & State Sync

### 7.1 Snapshot vs Event Replay

**Текущая реализация:**
- **Snapshot only:** `BattleHub.JoinBattle()` возвращает `BattleSnapshotDto`
- **Event replay:** **NOT FOUND** - нет истории событий

**Данные в snapshot:**
- `BattleSnapshotDto` (`src/Combats.Services.Battle/DTOs/BattleSnapshotDto.cs`):
  - `BattleId`, `PlayerAId`, `PlayerBId`, `Ruleset`
  - `Phase`, `TurnIndex`, `DeadlineUtc` (ISO string)
  - `NoActionStreakBoth`, `LastResolvedTurnIndex`, `Version`

**Проблемы:**
1. ❌ Клиент не знает историю действий (какие действия были отправлены)
2. ❌ Клиент не знает историю turns (какие turns уже разрешены)
3. ⚠️ `LastResolvedTurnIndex` есть в snapshot, но не детализация

### 7.2 Ограничение доступа

**Реализовано:**
1. ✅ **JWT authentication:** `[Authorize]` на `BattleHub`
2. ✅ **Participant check:** `PlayerAId == userId || PlayerBId == userId`
   - Код: `BattleHub.cs:66-72`

**НЕ реализовано:**
1. ❌ **Rate limiting:** нет ограничения на количество `JoinBattle` вызовов
2. ❌ **Concurrent connections:** нет ограничения на количество SignalR connections для одного battle

### 7.3 Риски Replay/Duplicate Commands

**Текущая защита:**
1. ✅ **Deadline validation:** действия после deadline → NoAction
2. ✅ **TurnIndex validation:** mismatch → NoAction для server turnIndex
3. ✅ **Phase validation:** не TurnOpen → NoAction

**НЕ защищено:**
1. ❌ **Replay атаки:** клиент может отправить одно и то же действие дважды (нет nonce/idempotency key)
2. ❌ **Concurrent submissions:** два SignalR connections от одного игрока могут отправить разные действия

**Рекомендации:**
- Добавить idempotency key в `SubmitTurnAction` (например, `actionId: Guid`)
- Сохранять `actionId` в Redis и проверять дубликаты

---

## 8. Observability

### 8.1 Логи

**Структурированное логирование:**

**Ключевые поля (реализовано):**
1. ✅ `BattleId` - во всех consumers и hub
2. ✅ `TurnIndex` - в `ResolveTurnConsumer`, `BattleHub`
3. ✅ `MessageId` - в consumers (через `context.MessageId`)
4. ✅ `CorrelationId` - в consumers (через `context.CorrelationId`)
5. ✅ `PlayerId` / `UserId` - в `BattleHub`
6. ⚠️ `MatchId` - не всегда логируется

**Примеры:**
- `CreateBattleConsumer.cs:26-28`: `BattleId`, `MatchId`
- `BattleCreatedEngineConsumer.cs:35-37`: `BattleId`, `MessageId`, `CorrelationId`
- `ResolveTurnConsumer.cs:37-39`: `BattleId`, `TurnIndex`, `MessageId`, `CorrelationId`
- `BattleHub.cs:37-39`: `UserId`, `BattleId`, `ConnectionId`

**Фильтр логирования:**
- `ConsumeLoggingFilter<T>` (`src/Combats.Infrastructure.Messaging/Filters/ConsumeLoggingFilter.cs`):
  - Логирует: `MessageId`, `MessageType`, `Endpoint`, `Consumer`, `CorrelationId`, `ConversationId`, `CausationId`
  - Duration: время обработки

### 8.2 Метрики

**ТЕКУЩИЙ СТАТУС:** **NOT FOUND** - нет явной реализации метрик (Prometheus/AppMetrics/etc).

**Ожидаемые метрики (минимум 10):**

1. ❌ `battle_turns_timeout_count` - количество turns, где deadline пропущен
2. ❌ `battle_invalid_payload_count` - количество invalid payloads (→ NoAction)
3. ❌ `battle_double_forfeit_count` - количество боев, завершенных DoubleForfeit
4. ❌ `battle_resolve_turn_duration_ms` - время обработки ResolveTurn
5. ❌ `battle_action_submit_duration_ms` - время обработки SubmitTurnAction
6. ❌ `battle_queue_latency_ms` - задержка сообщений в очереди (ResolveTurn)
7. ❌ `battle_active_count` - количество активных боев (gauge)
8. ❌ `battle_ended_count` - количество завершенных боев (counter)
9. ❌ `battle_watchdog_recovered_count` - количество восстановленных schedules
10. ❌ `battle_redis_operation_duration_ms` - время операций Redis (Lua scripts)

**Рекомендации:**
- Добавить AppMetrics или Prometheus.NET
- Инструментировать ключевые операции

### 8.3 Tracing

**ТЕКУЩИЙ СТАТУС:** **NOT FOUND** - нет явной реализации distributed tracing (OpenTelemetry/Application Insights).

**Где начинается trace:**
- Ожидаемо: в Gateway/BFF или в первом consumer
- Текущее: MassTransit может передавать headers, но нет явной инициализации

**Propagation:**
- MassTransit передает `CorrelationId`, `ConversationId`, `CausationId` автоматически
- Но нет явной интеграции с OpenTelemetry/Application Insights

**Рекомендации:**
- Добавить OpenTelemetry для distributed tracing
- Настроить propagation через MassTransit headers

---

## 9. Compliance Checklist

| Критерий | Статус | Комментарий |
|----------|--------|-------------|
| **A) Outbox atomicity** | ❌ **FAIL** | `CreateBattleConsumer` и `EndBattleConsumer` делают два `SaveChangesAsync()` - не атомарно. `ResolveTurnConsumer` публикует без `SaveChangesAsync()`. |
| **B) Inbox idempotency** | ✅ **PASS** | Реализовано через `InboxStore` с проверкой `(messageId, consumerId)`. |
| **C) At-least-once correctness** | ⚠️ **PARTIAL** | Consumers idempotent, но outbox не атомарен. SignalR actions не idempotent. |
| **D) State machines соответствуют** | ✅ **PASS** | Battle state machine реализован с инвариантами в Lua scripts. Matchmaking **NOT FOUND**. |
| **E) NoAction политика единообразна** | ✅ **PASS** | Все invalid scenarios → NoAction для server turnIndex. |
| **F) Reconnect реализован** | ✅ **PASS** | `JoinBattle()` возвращает snapshot. Но нет event replay. |
| **G) Security на SignalR** | ✅ **PASS** | `[Authorize]`, JWT validation, participant check. Но нет rate limiting. |
| **H) DLQ/poison strategy** | ❌ **FAIL** | Нет явной DLQ конфигурации. Только retry/redelivery. |
| **I) Redis recovery strategy** | ✅ **PASS** | Watchdog service восстанавливает missing schedules. Но нет персистентности Redis. |
| **J) Metrics/logging** | ⚠️ **PARTIAL** | Логирование есть (структурированное), но метрики **NOT FOUND**. Tracing **NOT FOUND**. |

---

## 10. Key Risks & Recommendations

### P0 (Критичные)

1. **Outbox не атомарен** (`CreateBattleConsumer`, `EndBattleConsumer`)
   - **Почему риск:** При падении между `Publish()` и вторым `SaveChangesAsync()` событие не сохранится в outbox, но battle создан.
   - **Решение:** Объединить в одну транзакцию: `Publish()` → `SaveChangesAsync()` (один раз).

2. **ResolveTurnConsumer публикует BattleEnded без outbox**
   - **Почему риск:** `ResolveTurnConsumer` не использует DbContext, поэтому `context.Publish()` не попадает в outbox. При падении события теряются.
   - **Решение:** Либо использовать DbContext в `ResolveTurnConsumer`, либо использовать отдельный outbox для событий из Redis-based consumers.

3. **Нет DLQ для poison messages**
   - **Почему риск:** После всех retries сообщение теряется или застревает в очереди, блокируя обработку.
   - **Решение:** Настроить DLQ exchange/queue в RabbitMQ, добавить мониторинг.

### P1 (Высокие)

4. **Matchmaking сервис отсутствует**
   - **Почему риск:** Нет полного flow от matchmaking до battle. Нельзя протестировать end-to-end.
   - **Решение:** Реализовать Matchmaking сервис с saga pattern.

5. **SignalR actions не idempotent**
   - **Почему риск:** Клиент может отправить одно действие дважды (replay атака или network retry).
   - **Решение:** Добавить `actionId: Guid` в `SubmitTurnAction`, проверять дубликаты в Redis.

6. **Нет метрик**
   - **Почему риск:** Невозможно мониторить производительность, выявлять проблемы (timeouts, invalid payloads).
   - **Решение:** Добавить AppMetrics/Prometheus.NET, инструментировать ключевые операции.

7. **Redis не персистентен**
   - **Почему риск:** При падении Redis все battle state теряется. Нет синхронизации с Postgres.
   - **Решение:** Включить Redis persistence (AOF/RDB) или добавить синхронизацию Redis → Postgres.

### P2 (Средние)

8. **Нет rate limiting на SignalR**
   - **Почему риск:** Возможны abuse атаки (множественные подключения, spam действий).
   - **Решение:** Добавить rate limiting middleware для SignalR (например, через Redis).

9. **Нет event replay для reconnect**
   - **Почему риск:** Клиент не знает историю после reconnect (какие действия были отправлены).
   - **Решение:** Либо добавить event store, либо расширить snapshot (включить историю действий).

10. **Нет distributed tracing**
    - **Почему риск:** Сложно отлаживать проблемы в production (нет полной картины flow).
    - **Решение:** Добавить OpenTelemetry, настроить propagation через MassTransit headers.

---

## Приложение: Ссылки на код

### Ключевые файлы

- **Consumers:**
  - `src/Combats.Services.Battle/Consumers/CreateBattleConsumer.cs`
  - `src/Combats.Services.Battle/Consumers/BattleCreatedEngineConsumer.cs`
  - `src/Combats.Services.Battle/Consumers/ResolveTurnConsumer.cs`
  - `src/Combats.Services.Battle/Consumers/EndBattleConsumer.cs`

- **State Store:**
  - `src/Combats.Services.Battle/State/RedisBattleStateStore.cs`
  - `src/Combats.Services.Battle/State/BattleState.cs`
  - `src/Combats.Services.Battle/State/BattlePhase.cs`

- **SignalR:**
  - `src/Combats.Services.Battle/Hubs/BattleHub.cs`

- **Infrastructure:**
  - `src/Combats.Infrastructure.Messaging/Inbox/InboxStore.cs`
  - `src/Combats.Infrastructure.Messaging/Inbox/InboxProcessor.cs`
  - `src/Combats.Infrastructure.Messaging/DependencyInjection/MessagingServiceCollectionExtensions.cs`

- **Services:**
  - `src/Combats.Services.Battle/Services/BattleWatchdogService.cs`

---

**Конец отчета**


