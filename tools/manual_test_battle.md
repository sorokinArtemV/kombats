# Manual Test: Battle Engine (2 Players)

Этот документ описывает ручной тест для проверки работы Battle Engine локально.

## Предварительные требования

1. **Запустить инфраструктуру:**
   ```bash
   # Postgres
   docker run -d --name postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=combats_battle -p 5432:5432 postgres:15

   # Redis
   docker run -d --name redis -p 6379:6379 redis:7-alpine

   # RabbitMQ (с delayed-message-exchange plugin)
   docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 \
     -e RABBITMQ_DEFAULT_USER=guest -e RABBITMQ_DEFAULT_PASS=guest \
     rabbitmq:3.12-management
   ```

2. **Запустить Battle сервис:**
   ```bash
   cd src/Combats.Services.Battle
   dotnet run
   ```
   Сервис должен запуститься на `https://localhost:5001` (или порт из launchSettings.json)

## Тестовый сценарий

### Шаг 1: Создать бой (DEV endpoint)

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

**Ожидаемый ответ:**
```json
{
  "battleId": "xxx-xxx-xxx",
  "matchId": "yyy-yyy-yyy"
}
```

Сохраните `battleId` для следующих шагов.

### Шаг 2: Подключить двух клиентов к SignalR

Используйте следующий HTML/JS код в браузере (откройте два окна/вкладки):

```html
<!DOCTYPE html>
<html>
<head>
    <title>Battle Test Client</title>
    <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@latest/dist/browser/signalr.min.js"></script>
</head>
<body>
    <h1>Battle Test Client</h1>
    <div>
        <label>Player ID (GUID):</label>
        <input type="text" id="playerId" value="11111111-1111-1111-1111-111111111111" />
    </div>
    <div>
        <label>Battle ID:</label>
        <input type="text" id="battleId" />
    </div>
    <button onclick="connect()">Connect</button>
    <button onclick="joinBattle()">Join Battle</button>
    <button onclick="submitAction()">Submit Action</button>
    <div id="log"></div>

    <script>
        let connection = null;
        const log = (msg) => {
            const div = document.getElementById('log');
            div.innerHTML += '<div>' + new Date().toISOString() + ': ' + msg + '</div>';
        };

        function connect() {
            const playerId = document.getElementById('playerId').value;
            const url = 'https://localhost:5001/battlehub?playerId=' + playerId;
            
            connection = new signalR.HubConnectionBuilder()
                .withUrl(url, {
                    skipNegotiation: true,
                    transport: signalR.HttpTransportType.WebSockets
                })
                .build();

            connection.on('BattleReady', (data) => {
                log('BattleReady: ' + JSON.stringify(data));
            });

            connection.on('TurnOpened', (data) => {
                log('TurnOpened: ' + JSON.stringify(data));
            });

            connection.on('BattleEnded', (data) => {
                log('BattleEnded: ' + JSON.stringify(data));
            });

            connection.start()
                .then(() => log('Connected to SignalR'))
                .catch(err => log('Error: ' + err));
        }

        async function joinBattle() {
            const battleId = document.getElementById('battleId').value;
            try {
                const snapshot = await connection.invoke('JoinBattle', battleId);
                log('Snapshot: ' + JSON.stringify(snapshot));
            } catch (err) {
                log('JoinBattle error: ' + err);
            }
        }

        async function submitAction() {
            const battleId = document.getElementById('battleId').value;
            const action = JSON.stringify({ actionType: 'attack', value: 100 });
            try {
                await connection.invoke('SubmitTurnAction', battleId, 1, action);
                log('Action submitted');
            } catch (err) {
                log('SubmitTurnAction error: ' + err);
            }
        }
    </script>
</body>
</html>
```

**Инструкция:**
1. Откройте два окна браузера
2. В первом окне: Player ID = `11111111-1111-1111-1111-111111111111`
3. Во втором окне: Player ID = `22222222-2222-2222-2222-222222222222`
4. В обоих окнах: вставьте `battleId` из шага 1
5. Нажмите "Connect" в обоих окнах
6. Нажмите "Join Battle" в обоих окнах
7. В первом окне нажмите "Submit Action" (отправит действие)
8. Во втором окне ничего не делайте (NoAction)
9. Подождите ~10 секунд - должен открыться следующий turn
10. Повторите шаг 7-8 еще 2 раза (всего 3 turn подряд с NoAction)
11. После 3-го turn с NoAction должен прийти `BattleEnded` с `Reason: "DoubleForfeit"`

### Шаг 3: Проверить логи сервера

В логах Battle сервиса должны быть:
- `CreateBattleConsumer` обработал команду
- `BattleCreatedEngineConsumer` инициализировал state, открыл Turn 1
- `ResolveTurnConsumer` разрешил turn, открыл следующий
- После 3 NoAction подряд: `BattleEnded` опубликован

### Шаг 4: Проверить DoubleForfeit

После 3 подряд NoAction turns:
- Оба клиента должны получить `BattleEnded` событие
- `JoinBattle` должен вернуть snapshot с `Phase: "Ended"`, `EndedReason: "DoubleForfeit"`
- `SubmitTurnAction` должен вернуть ошибку "Battle has ended"

## Альтернативный способ: Node.js скрипт

Создайте файл `tools/test_battle.js`:

```javascript
const signalR = require('@microsoft/signalr');

const BATTLE_ID = process.argv[2];
const PLAYER_ID = process.argv[3];

if (!BATTLE_ID || !PLAYER_ID) {
    console.error('Usage: node test_battle.js <battleId> <playerId>');
    process.exit(1);
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl(`https://localhost:5001/battlehub?playerId=${PLAYER_ID}`, {
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
    })
    .build();

connection.on('BattleReady', (data) => {
    console.log('BattleReady:', JSON.stringify(data));
});

connection.on('TurnOpened', (data) => {
    console.log('TurnOpened:', JSON.stringify(data));
});

connection.on('BattleEnded', (data) => {
    console.log('BattleEnded:', JSON.stringify(data));
    process.exit(0);
});

connection.start()
    .then(async () => {
        console.log('Connected');
        const snapshot = await connection.invoke('JoinBattle', BATTLE_ID);
        console.log('Snapshot:', JSON.stringify(snapshot));
        
        // Submit action for turn 1
        await connection.invoke('SubmitTurnAction', BATTLE_ID, 1, JSON.stringify({ actionType: 'attack' }));
        console.log('Action submitted for turn 1');
    })
    .catch(err => {
        console.error('Error:', err);
        process.exit(1);
    });
```

**Запуск:**
```bash
# Терминал 1 (Player A)
node tools/test_battle.js <battleId> 11111111-1111-1111-1111-111111111111

# Терминал 2 (Player B) - не отправляет действия
node tools/test_battle.js <battleId> 22222222-2222-2222-2222-222222222222
```

## Проверка watchdog

Чтобы проверить watchdog recovery:
1. Создайте бой
2. Остановите RabbitMQ: `docker stop rabbitmq`
3. Подождите 5-10 секунд (watchdog сканирует каждые 5 секунд)
4. Запустите RabbitMQ: `docker start rabbitmq`
5. Watchdog должен обнаружить missing schedule и reschedule `ResolveTurn`

## Troubleshooting

- **SignalR connection fails:** Убедитесь, что используется `?playerId=...` в query string или `X-Player-Id` header
- **Battle not found:** Проверьте, что `BattleCreatedEngineConsumer` обработал событие (логи)
- **ResolveTurn не выполняется:** Проверьте RabbitMQ delayed-message-exchange plugin, логи watchdog
- **DoubleForfeit не срабатывает:** Проверьте `NoActionLimit` в Ruleset (должен быть 3)



