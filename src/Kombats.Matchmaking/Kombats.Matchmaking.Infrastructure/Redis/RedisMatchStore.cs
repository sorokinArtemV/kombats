using System.Text.Json;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kombats.Matchmaking.Infrastructure.Redis;

/// <summary>
/// Infrastructure implementation of IMatchStore using Redis.
/// Stores match records as JSON strings with TTL.
/// </summary>
// public class RedisMatchStore : IMatchStore
// {
//     private readonly IConnectionMultiplexer _redis;
//     private readonly ILogger<RedisMatchStore> _logger;
//     private readonly MatchmakingRedisOptions _options;
//
//     private static readonly JsonSerializerOptions JsonOptions = new()
//     {
//         PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
//         WriteIndented = false
//     };
//
//     public RedisMatchStore(
//         IConnectionMultiplexer redis,
//         ILogger<RedisMatchStore> logger,
//         IOptions<MatchmakingRedisOptions> options)
//     {
//         _redis = redis;
//         _logger = logger;
//         _options = options.Value;
//     }
//
//     private IDatabase GetDatabase() => _redis.GetDatabase(_options.DatabaseIndex);
//
//     private string GetMatchKey(Guid matchId) => $"mm:match:{matchId}";
//
//     public async Task StoreMatchAsync(MatchRecord match, CancellationToken cancellationToken = default)
//     {
//         var db = GetDatabase();
//         var key = GetMatchKey(match.MatchId);
//
//         try
//         {
//             var stored = new StoredMatchRecord
//             {
//                 MatchId = match.MatchId,
//                 BattleId = match.BattleId,
//                 PlayerAId = match.PlayerAId,
//                 PlayerBId = match.PlayerBId,
//                 Variant = match.Variant,
//                 CreatedAtUtcUnixMs = match.CreatedAtUtc.ToUnixTimeMilliseconds()
//             };
//
//             var json = JsonSerializer.Serialize(stored, JsonOptions);
//             await db.StringSetAsync(key, json, TimeSpan.FromSeconds(_options.StatusTtlSeconds));
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex,
//                 "Error in StoreMatchAsync for MatchId: {MatchId}",
//                 match.MatchId);
//             throw;
//         }
//     }
//
//     public async Task<MatchRecord?> GetMatchAsync(Guid matchId, CancellationToken cancellationToken = default)
//     {
//         var db = GetDatabase();
//         var key = GetMatchKey(matchId);
//
//         try
//         {
//             var json = await db.StringGetAsync(key);
//             if (!json.HasValue)
//             {
//                 return null;
//             }
//
//             var stored = JsonSerializer.Deserialize<StoredMatchRecord>(json.ToString(), JsonOptions);
//             if (stored == null)
//             {
//                 _logger.LogError(
//                     "Deserialized match record is null for MatchId: {MatchId}",
//                     matchId);
//                 return null;
//             }
//
//             return new MatchRecord
//             {
//                 MatchId = stored.MatchId,
//                 BattleId = stored.BattleId,
//                 PlayerAId = stored.PlayerAId,
//                 PlayerBId = stored.PlayerBId,
//                 Variant = stored.Variant,
//                 CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(stored.CreatedAtUtcUnixMs)
//             };
//         }
//         catch (JsonException ex)
//         {
//             _logger.LogError(ex,
//                 "Failed to deserialize match record for MatchId: {MatchId}",
//                 matchId);
//             return null;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex,
//                 "Error in GetMatchAsync for MatchId: {MatchId}",
//                 matchId);
//             throw;
//         }
//     }
//
//     /// <summary>
//     /// Stored representation of match record (for JSON serialization).
//     /// Uses unix milliseconds for timestamp to avoid timezone issues.
//     /// </summary>
//     private class StoredMatchRecord
//     {
//         public Guid MatchId { get; set; }
//         public Guid BattleId { get; set; }
//         public Guid PlayerAId { get; set; }
//         public Guid PlayerBId { get; set; }
//         public string Variant { get; set; } = string.Empty;
//         public long CreatedAtUtcUnixMs { get; set; }
//     }
// }

