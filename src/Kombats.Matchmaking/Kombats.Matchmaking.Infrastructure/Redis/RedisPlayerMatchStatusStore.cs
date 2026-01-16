using System.Text.Json;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Infrastructure implementation of IPlayerMatchStatusStore using Redis.
/// Stores player status as JSON strings with TTL.
/// </summary>
public class RedisPlayerMatchStatusStore : IPlayerMatchStatusStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPlayerMatchStatusStore> _logger;
    private readonly MatchmakingRedisOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisPlayerMatchStatusStore(
        IConnectionMultiplexer redis,
        ILogger<RedisPlayerMatchStatusStore> logger,
        IOptions<MatchmakingRedisOptions> options)
    {
        _redis = redis;
        _logger = logger;
        _options = options.Value;
    }

    private IDatabase GetDatabase() => _redis.GetDatabase(_options.DatabaseIndex);

    private string GetPlayerStatusKey(Guid playerId) => $"mm:player:{playerId}";

    public async Task<PlayerMatchStatus?> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetPlayerStatusKey(playerId);

        try
        {
            var json = await db.StringGetAsync(key);
            if (!json.HasValue)
            {
                return null;
            }

            var status = JsonSerializer.Deserialize<StoredPlayerMatchStatus>(json.ToString(), JsonOptions);
            if (status == null)
            {
                _logger.LogError(
                    "Deserialized player status is null for PlayerId: {PlayerId}",
                    playerId);
                return null;
            }

            return new PlayerMatchStatus
            {
                State = status.State,
                MatchId = status.MatchId,
                BattleId = status.BattleId,
                Variant = status.Variant,
                UpdatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(status.UpdatedAtUtcUnixMs)
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize player status for PlayerId: {PlayerId}",
                playerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in GetStatusAsync for PlayerId: {PlayerId}",
                playerId);
            throw;
        }
    }

    public async Task SetSearchingAsync(string variant, Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetPlayerStatusKey(playerId);

        try
        {
            var status = new StoredPlayerMatchStatus
            {
                State = PlayerMatchState.Searching,
                MatchId = null,
                BattleId = null,
                Variant = variant,
                UpdatedAtUtcUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var json = JsonSerializer.Serialize(status, JsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromSeconds(_options.StatusTtlSeconds));
            
            _logger.LogInformation(
                "Player status set to Searching: PlayerId={PlayerId}, Variant={Variant}, DatabaseIndex={DatabaseIndex}, TtlSeconds={TtlSeconds}",
                playerId, variant, _options.DatabaseIndex, _options.StatusTtlSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in SetSearchingAsync for PlayerId: {PlayerId}, Variant: {Variant}, DatabaseIndex={DatabaseIndex}",
                playerId, variant, _options.DatabaseIndex);
            throw;
        }
    }

    public async Task SetMatchedAsync(Guid playerId, Guid matchId, Guid battleId, string variant, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetPlayerStatusKey(playerId);

        try
        {
            var status = new StoredPlayerMatchStatus
            {
                State = PlayerMatchState.Matched,
                MatchId = matchId,
                BattleId = battleId,
                Variant = variant,
                UpdatedAtUtcUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var json = JsonSerializer.Serialize(status, JsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromSeconds(_options.StatusTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in SetMatchedAsync for PlayerId: {PlayerId}, MatchId: {MatchId}, BattleId: {BattleId}",
                playerId, matchId, battleId);
            throw;
        }
    }

    public async Task RemoveStatusAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetPlayerStatusKey(playerId);

        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in RemoveStatusAsync for PlayerId: {PlayerId}",
                playerId);
            throw;
        }
    }

    /// <summary>
    /// Stored representation of player match status (for JSON serialization).
    /// Uses unix milliseconds for timestamp to avoid timezone issues.
    /// </summary>
    private class StoredPlayerMatchStatus
    {
        public PlayerMatchState State { get; set; }
        public Guid? MatchId { get; set; }
        public Guid? BattleId { get; set; }
        public string Variant { get; set; } = string.Empty;
        public long UpdatedAtUtcUnixMs { get; set; }
    }
}

