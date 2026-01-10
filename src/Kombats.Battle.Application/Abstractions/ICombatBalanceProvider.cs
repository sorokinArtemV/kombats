using Kombats.Battle.Domain.Rules;

namespace Kombats.Battle.Application.Abstractions;

/// <summary>
/// Port interface for providing CombatBalance configuration.
/// Application uses this to get balance settings without depending on Infrastructure.
/// </summary>
public interface ICombatBalanceProvider
{
    /// <summary>
    /// Gets the current CombatBalance configuration.
    /// </summary>
    CombatBalance GetBalance();
}


