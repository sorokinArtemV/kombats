using Combats.Battle.Domain.Model;
using Combats.Battle.Domain.Results;
using Combats.Battle.Domain.Rules;
using Combats.Battle.Realtime.Contracts;
using Microsoft.Extensions.Logging;

namespace Combats.Battle.Infrastructure.Realtime.SignalR;

/// <summary>
/// Mapper for converting Domain/Application types to Realtime contracts.
/// This maintains boundary independence between Infrastructure and Domain.
/// </summary>
internal static class RealtimeContractMapper
{
    /// <summary>
    /// Maps Domain.Rules.Ruleset to BattleRulesetRealtime.
    /// Only includes fields needed by UI clients.
    /// </summary>
    public static BattleRulesetRealtime ToRealtimeRuleset(Ruleset ruleset)
    {
        return new BattleRulesetRealtime
        {
            TurnSeconds = ruleset.TurnSeconds,
            NoActionLimit = ruleset.NoActionLimit
        };
    }

    /// <summary>
    /// Maps BattlePhase enum (as string) to BattlePhaseRealtime.
    /// Throws ArgumentException if phase string is unknown.
    /// </summary>
    public static BattlePhaseRealtime ToRealtimePhase(string phase, ILogger? logger = null)
    {
        // Try parsing as BattlePhase enum first
        if (Enum.TryParse<BattlePhase>(phase, ignoreCase: true, out var battlePhase))
        {
            return battlePhase switch
            {
                BattlePhase.ArenaOpen => BattlePhaseRealtime.ArenaOpen,
                BattlePhase.TurnOpen => BattlePhaseRealtime.TurnOpen,
                BattlePhase.Resolving => BattlePhaseRealtime.Resolving,
                BattlePhase.Ended => BattlePhaseRealtime.Ended,
                _ => throw new ArgumentException($"Unknown BattlePhase value: {battlePhase}", nameof(phase))
            };
        }

        // Fallback: try direct string matching
        var phaseUpper = phase.ToUpperInvariant();
        var result = phaseUpper switch
        {
            "ARENAOPEN" or "ARENA_OPEN" => BattlePhaseRealtime.ArenaOpen,
            "TURNOPEN" or "TURN_OPEN" => BattlePhaseRealtime.TurnOpen,
            "RESOLVING" => BattlePhaseRealtime.Resolving,
            "ENDED" => BattlePhaseRealtime.Ended,
            _ => throw new ArgumentException($"Unknown phase string: {phase}", nameof(phase))
        };

        logger?.LogWarning("Mapped phase string '{Phase}' to BattlePhaseRealtime using fallback matching", phase);
        return result;
    }

    /// <summary>
    /// Maps EndBattleReason enum (as string) to BattleEndReasonRealtime.
    /// Returns Unknown if reason string is null or cannot be parsed.
    /// </summary>
    public static BattleEndReasonRealtime? ToRealtimeEndReason(string? reason, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;

        // Try parsing as EndBattleReason enum first
        if (Enum.TryParse<EndBattleReason>(reason, ignoreCase: true, out var endReason))
        {
            return endReason switch
            {
                EndBattleReason.Normal => BattleEndReasonRealtime.Normal,
                EndBattleReason.DoubleForfeit => BattleEndReasonRealtime.DoubleForfeit,
                EndBattleReason.Timeout => BattleEndReasonRealtime.Timeout,
                EndBattleReason.Cancelled => BattleEndReasonRealtime.Cancelled,
                EndBattleReason.AdminForced => BattleEndReasonRealtime.AdminForced,
                EndBattleReason.SystemError => BattleEndReasonRealtime.SystemError,
                _ => BattleEndReasonRealtime.Unknown
            };
        }

        // Fallback: try direct string matching
        var reasonUpper = reason.ToUpperInvariant();
        var result = reasonUpper switch
        {
            "NORMAL" => BattleEndReasonRealtime.Normal,
            "DOUBLEFORFEIT" or "DOUBLE_FORFEIT" => BattleEndReasonRealtime.DoubleForfeit,
            "TIMEOUT" => BattleEndReasonRealtime.Timeout,
            "CANCELLED" or "CANCELED" => BattleEndReasonRealtime.Cancelled,
            "ADMINFORCED" or "ADMIN_FORCED" => BattleEndReasonRealtime.AdminForced,
            "SYSTEMERROR" or "SYSTEM_ERROR" => BattleEndReasonRealtime.SystemError,
            _ => BattleEndReasonRealtime.Unknown
        };

        if (result == BattleEndReasonRealtime.Unknown)
        {
            logger?.LogWarning("Could not map end reason string '{Reason}' to BattleEndReasonRealtime, using Unknown", reason);
        }

        return result;
    }
}

