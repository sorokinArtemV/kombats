# Battle Engine - Локальный запуск и тестирование

Этот документ описывает, как запустить и протестировать Battle Engine локально без полноценных Auth и Matchmaking сервисов.

## ⚠️ ВАЖНО: DEV-ONLY функции

Следующие функции работают **ТОЛЬКО в Development окружении**:
- `POST /dev/battles` - создание боя без Matchmaking
- `DevSignalRAuthMiddleware` - аутентификация через `X-Player-Id` header

В Production эти функции **отключены** и не доступны.

## Быстрый старт

### 1. Запустить инфраструктуру

```bash
# Postgres
docker run -d --name postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=combats_battle -p 5432:5432 postgres:15

# Redis
docker run -d --name redis -p 6379:6379 redis:7-alpine

# RabbitMQ (с delayed-message-exchange plugin)
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 \
  -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest \
  rabbitmq:3.12-management

# Включить delayed-message-exchange plugin
docker exec rabbitmq rabbitmq-plugins enable rabbitmq_delayed_message_exchange
```

### 2. Запустить Battle сервис

```bash
cd src/Combats.Services.Battle
dotnet run
```

Сервис запустится на `https://localhost:5001` (или порт из `launchSettings.json`).

### 3. Создать бой (DEV endpoint)

```bash
curl -X POST https://localhost:5001/dev/battles \
  -H "Content-Type: application/json" \
  -d '{
    "playerAId": "11111111-1111-1111-1111-111111111111",
    "playerBId": "22222222-2222-2222-2222-222222222222",
    "turnSeconds": 10,
    "noActionLimit": 3
  }' \
  -k
```

**Ответ:**
```json
{
  "battleId": "xxx-xxx-xxx",
  "matchId": "yyy-yyy-yyy"
}
```

### 4. Подключить клиентов к SignalR

**Вариант A: Браузер (HTML/JS)**

Откройте `tools/manual_test_battle.md` для полного HTML примера.

**Вариант B: curl + wscat (WebSocket)**

```bash
# Установить wscat: npm install -g wscat

# Подключение Player A
wscat -c "wss://localhost:5001/battlehub?playerId=11111111-1111-1111-1111-111111111111" \
  --no-check

# В wscat терминале:
> {"protocol": "json", "version": 1}
< {"type":1}
> {"type":1,"invocationId":"1","target":"JoinBattle","arguments":["<battleId>"]}
```

## Архитектура flow

1. **POST /dev/battles** → отправляет `CreateBattle` command через MassTransit
2. **CreateBattleConsumer** → создает `BattleEntity` в Postgres, публикует `BattleCreated`
3. **BattleCreatedEngineConsumer** → инициализирует `BattleState` в Redis, открывает Turn 1, планирует `ResolveTurn`
4. **ResolveTurnConsumer** → разрешает turn, открывает следующий, планирует следующий `ResolveTurn`
5. **BattleWatchdogService** → сканирует активные бои каждые 5 секунд, восстанавливает missing schedules

## SignalR Endpoints

- **Hub URL:** `https://localhost:5001/battlehub`
- **DEV Auth:** Передайте `X-Player-Id` header или `playerId` query parameter (GUID)
- **Methods:**
  - `JoinBattle(battleId: Guid)` → возвращает `BattleSnapshotDto`
  - `SubmitTurnAction(battleId: Guid, turnIndex: int, actionPayload: string)`
- **Events (server → client):**
  - `BattleReady` → когда бой готов
  - `TurnOpened` → когда открыт новый turn
  - `BattleEnded` → когда бой завершен

## Проверка работы

### Сценарий 1: Нормальный flow

1. Создать бой
2. Оба игрока подключаются и join
3. Оба отправляют действия
4. Turn продвигается каждые ~10 секунд

### Сценарий 2: DoubleForfeit

1. Создать бой
2. Оба игрока подключаются
3. Один игрок отправляет действия, второй молчит (или оба молчат)
4. После 3 подряд NoAction turns → `BattleEnded` с `Reason: "DoubleForfeit"`

### Сценарий 3: Reconnect

1. Создать бой, оба join
2. Один игрок отключается
3. Игрок переподключается и вызывает `JoinBattle`
4. Должен получить актуальный snapshot

## Конфигурация

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=combats_battle;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "Messaging": {
    "RabbitMq": {
      "Host": "localhost",
      "VirtualHost": "/",
      "Username": "guest",
      "Password": "guest"
    }
  }
}
```

### Environment

Убедитесь, что `ASPNETCORE_ENVIRONMENT=Development` для работы dev-only функций.

## Troubleshooting

- **"Battle not found"** → Проверьте, что `BattleCreatedEngineConsumer` обработал событие (логи)
- **ResolveTurn не выполняется** → Проверьте RabbitMQ delayed-message-exchange plugin, логи watchdog
- **SignalR connection fails** → Убедитесь, что передаете `playerId` в query string или header
- **DoubleForfeit не срабатывает** → Проверьте `NoActionLimit` в Ruleset (по умолчанию 3)

## См. также

- `tools/manual_test_battle.md` - детальный manual test сценарий
- `docs/audit_report.md` - полный аудит архитектуры






