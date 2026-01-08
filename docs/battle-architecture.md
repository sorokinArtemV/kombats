# Battle Service Architecture

This document describes the architecture of the Combats battle service, including layer responsibilities, key invariants, idempotency rules, and configuration knobs.

## Layer Responsibilities

### Combats.Battle.Api
**Thin wrapper layer** - Controllers, Hubs, and Workers are thin adapters that delegate to Application services.

- **Controllers** (`DevBattlesController`): HTTP endpoints that delegate to Application
- **Hubs** (`BattleHub`): SignalR hub that wraps Application services
- **Workers** (`TurnDeadlineWorker`): Background worker that triggers turn resolution
- **Responsibilities**: Authentication, transport concerns, DI registration
- **MUST NOT**: Contain business logic, rules validation, or protocol normalization

### Combats.Battle.Application
**Orchestration and coordination layer** - Owns battle rules, protocol normalization, and business workflows.

**Key Components:**
- **Services/** (`BattleLifecycleAppService`, `BattleTurnAppService`): Orchestrate battle operations
- **Rules/** (`RulesetNormalizer`, `BattleRulesDefaults`): Single source of truth for battle rules and validation
- **Protocol/** (`PlayerActionNormalizer`): Protocol-level validation and normalization
- **Policies/** (`Policies/Time/TurnDeadlinePolicy`): Policies for time-based decisions
- **Mapping/** (`BattleStateToDomainMapper`): Maps Application views to Domain models
- **Ports/**: Interfaces for Infrastructure to implement (`IBattleStateStore`, `IBattleRealtimeNotifier`, etc.)

**Responsibilities:**
- Battle lifecycle orchestration
- Ruleset normalization and validation (bounds enforcement)
- Protocol validation (turn index, phase, deadline)
- State machine enforcement
- Coordinates Domain, Infrastructure, and external notifications

**MUST NOT**: Contain Redis/SignalR/EF/MassTransit specifics, domain combat mechanics

### Combats.Battle.Domain
**Pure domain logic** - No infrastructure dependencies.

**Key Components:**
- **Engine/** (`BattleEngine`): Pure function for resolving turns (combat mechanics)
- **Model/** (`BattleDomainState`, `PlayerAction`, `PlayerState`): Domain entities
- **Events/** (`IDomainEvent`, `BattleEndedDomainEvent`, `TurnResolvedDomainEvent`): Domain events
- **Rules/**: Domain-level rules (zone adjacency, action validation)

**Responsibilities:**
- Turn resolution logic (combat mechanics)
- Domain invariants and validation
- Event generation

**MUST NOT**: Contain any infrastructure dependencies (Redis, SignalR, EF, MassTransit, Configuration)

### Combats.Battle.Infrastructure
**Implementation layer** - Implements Application ports.

**Key Components:**
- **State/Redis/**: Redis implementation of `IBattleStateStore` (with Mapping)
- **Messaging/**: MassTransit consumers and publisher
- **Persistence/EF/**: Entity Framework DbContext, entities, migrations
- **Realtime/SignalR/**: SignalR implementation of `IBattleRealtimeNotifier`
- **Profiles/**: Profile provider implementations
- **Time/**: Clock implementation

**Responsibilities:**
- Implement Application ports
- Handle infrastructure concerns (serialization, persistence, messaging)
- Map between infrastructure and application models

**MUST NOT**: Contain battle rules, protocol normalization, or domain logic

---

## Where to Change What

### Combat Mechanics
**Location**: `Combats.Battle.Domain/Engine/BattleEngine.cs`

- Damage calculation
- Zone-based combat logic
- HP/stamina interactions
- Battle end conditions (DoubleForfeit, HP depletion)

**Example**: To change how damage is calculated, modify `BattleEngine.ResolveTurn()`.

### Protocol Normalization
**Location**: `Combats.Battle.Application/Protocol/PlayerActionNormalizer.cs`

- Turn index validation
- Phase validation
- Deadline validation
- JSON payload validation
- NoAction conversion rules

**Example**: To add a new validation rule for action payloads, modify `PlayerActionNormalizer.NormalizeActionPayload()`.

### Battle Rules / Configuration
**Location**: `Combats.Battle.Application/Rules/`

- **RulesetNormalizer**: Normalizes and validates Ruleset (applies defaults, enforces bounds)
- **BattleRulesDefaults**: Default values and validation bounds

**Key Configuration Knobs:**
- `TurnSeconds`: Time per turn (bounds: 1-60 seconds, default: 10)
- `NoActionLimit`: Consecutive NoAction turns before DoubleForfeit (bounds: 1-10, default: 3)
- `HpPerStamina`: HP multiplier for stamina (default: 10, min: 1)
- `DamagePerStrength`: Damage multiplier for strength (default: 2, min: 1)

**Example**: To change the maximum turn duration, modify `BattleRulesDefaults.MaxTurnSeconds`.

**Note**: Ruleset is normalized in `BattleLifecycleAppService.HandleBattleCreatedAsync()` when initializing battle state. All subsequent operations use the normalized ruleset.

### Time Policies
**Location**: `Combats.Battle.Application/Policies/Time/TurnDeadlinePolicy.cs`

- Deadline resolution policy (skew tolerance)

**Worker Configuration**:
- `TurnDeadlineWorker`: Poll interval (default: 300ms), batch size (default: 50), skew (default: 100ms)
- Located in `Combats.Battle.Api/Workers/TurnDeadlineWorker.cs`

---

## Key Invariants and Idempotency Rules

### State Machine Invariants

**Phases**: `ArenaOpen` → `TurnOpen` → `Resolving` → `TurnOpen` (continue) or `Ended` (terminate)

**Enforced in Redis Lua scripts** (atomic operations):

1. **Cannot open turn N if `LastResolvedTurnIndex != N-1`**
2. **Cannot open turn if `Phase == Ended`**
3. **Cannot open turn if `Phase != ArenaOpen && Phase != Resolving`**
4. **Cannot transition to Resolving if `Phase != TurnOpen`**
5. **Cannot resolve turn if `Phase != Resolving`**
6. **Cannot end battle if `Phase != Resolving`**

### Idempotency Rules

#### Action Storage (First-Write-Wins)
**Location**: `RedisBattleStateStore.StoreActionAsync()`

- Uses Redis `SET NX` (SET if Not eXists) for atomic first-write-wins
- **Result**: `ActionStoreResult.Accepted` (first write) or `ActionStoreResult.AlreadySubmitted` (duplicate)
- **Behavior**: Duplicate submissions are ignored (idempotent)

**Example**: If a player submits the same action twice (network retry), only the first write is accepted.

#### Turn Resolution
**Location**: `BattleTurnAppService.ResolveTurnAsync()`

- **Idempotency check**: If `turnIndex <= LastResolvedTurnIndex`, return false (already resolved)
- **CAS transition**: `TryMarkTurnResolvingAsync()` uses atomic compare-and-swap
- **Behavior**: Safe to call multiple times; only one resolution succeeds

#### Battle Ending
**Location**: `RedisBattleStateStore.EndBattleAndMarkResolvedAsync()`

- Returns `EndBattleCommitResult`:
  - `EndedNow` (1): Battle transitioned to Ended in this call
  - `AlreadyEnded` (2): Battle was already in Ended phase (idempotent)
  - `NotCommitted` (0): Could not end (wrong phase/turn)

**Critical**: Only publish `BattleEnded` event and send notifications when result is `EndedNow`. When `AlreadyEnded`, skip notifications to avoid duplicate events.

**Example**:
```csharp
var endResult = await _stateStore.EndBattleAndMarkResolvedAsync(...);
if (endResult == EndBattleCommitResult.EndedNow)
{
    // Only notify/publish if battle ended in this call
    await _notifier.NotifyBattleEndedAsync(...);
    await _eventPublisher.PublishBattleEndedAsync(...);
}
else if (endResult == EndBattleCommitResult.AlreadyEnded)
{
    // Already ended - skip notifications (idempotent case)
}
```

#### Battle Initialization
**Location**: `RedisBattleStateStore.TryInitializeBattleAsync()`

- Uses Redis `SET NX` for atomic initialization
- **Behavior**: If battle already initialized, return false (idempotent)
- Safe to call multiple times (handles duplicate `BattleCreated` events)

---

## Configuration Knobs

### Battle Rules (Application Layer)

**Location**: `Combats.Battle.Application/Rules/BattleRulesDefaults.cs`

| Parameter | Default | Min | Max | Description |
|-----------|---------|-----|-----|-------------|
| `DefaultTurnSeconds` | 10 | 1 | 60 | Time per turn in seconds |
| `DefaultNoActionLimit` | 3 | 1 | 10 | Consecutive NoAction turns before DoubleForfeit |
| `DefaultHpPerStamina` | 10 | 1 | - | HP multiplier for stamina stat |
| `DefaultDamagePerStrength` | 2 | 1 | - | Damage multiplier for strength stat |

**Enforcement**: `RulesetNormalizer` enforces bounds when normalizing incoming rulesets.

**Usage**: Ruleset is normalized once during battle initialization. All subsequent operations use the normalized ruleset.

### Worker Configuration (Api Layer)

**Location**: `Combats.Battle.Api/Workers/TurnDeadlineWorker.cs`

| Parameter | Value | Description |
|-----------|-------|-------------|
| `PollIntervalMs` | 300 | Milliseconds between deadline checks |
| `BatchSize` | 50 | Maximum battles processed per iteration |
| `SkewMs` | 100 | Clock skew buffer for deadline resolution |

**Policy**: `TurnDeadlinePolicy.ShouldResolve()` only resolves when `now >= deadline + skewMs`.

---

## Data Flow

### Battle Initialization
1. `CreateBattleConsumer` receives `CreateBattle` command
2. Creates battle entity in PostgreSQL
3. Publishes `BattleCreated` event
4. `BattleLifecycleAppService.HandleBattleCreatedAsync()`:
   - Normalizes ruleset using `RulesetNormalizer` (applies defaults, enforces bounds)
   - Gets player profiles
   - Initializes battle state in Redis (atomic SET NX)
   - Opens Turn 1
   - Notifies clients via SignalR

### Turn Resolution Flow
1. **Action Submission** (`BattleTurnAppService.SubmitActionAsync()`):
   - Validates protocol (phase, turn index, deadline) via `PlayerActionNormalizer`
   - Stores action in Redis (first-write-wins)
   - If both players have actions, triggers early resolution

2. **Turn Resolution** (`BattleTurnAppService.ResolveTurnAsync()`):
   - Idempotency check: `turnIndex <= LastResolvedTurnIndex` → skip
   - Atomic CAS: transition to `Resolving` phase
   - Read actions from Redis
   - Parse actions into domain objects
   - Resolve turn using `BattleEngine` (pure domain logic)
   - Process domain events:
     - `BattleEndedDomainEvent`: Commit battle end, notify clients, publish integration event
     - `TurnResolvedDomainEvent`: Open next turn, notify clients

3. **Deadline Worker** (`TurnDeadlineWorker`):
   - Polls Redis ZSET for expired deadlines
   - Calls `ResolveTurnAsync()` for each due battle
   - Uses `TurnDeadlinePolicy` to check deadline + skew

### Notification Flow
- **SignalR**: `SignalRBattleRealtimeNotifier` implements `IBattleRealtimeNotifier`
- **Integration Events**: `MassTransitBattleEventPublisher` implements `IBattleEventPublisher`
- Events are sent only when state transitions occur (not on idempotent operations)

---

## Key Design Principles

1. **Single Source of Truth**: Battle rules are normalized once during initialization via `RulesetNormalizer`
2. **Idempotency**: All state transitions are idempotent (safe to retry)
3. **Atomic Operations**: Critical transitions use Redis Lua scripts for atomicity
4. **Layer Separation**: Domain is pure (no infrastructure), Infrastructure implements Application ports
5. **First-Write-Wins**: Actions use Redis SET NX to prevent duplicate submissions
6. **Event-Driven**: Domain events drive state transitions and notifications

---

## Testing Considerations

- **Application Services**: Can be unit tested with mock ports
- **Domain Engine**: Pure functions, easily testable
- **Infrastructure**: Integration tests with Redis/PostgreSQL
- **Idempotency**: Critical to test duplicate operations (retries, race conditions)


