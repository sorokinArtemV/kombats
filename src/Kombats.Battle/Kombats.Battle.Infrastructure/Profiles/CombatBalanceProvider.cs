using Kombats.Battle.Application.Abstractions;
using Kombats.Battle.Domain.Rules;
using Microsoft.Extensions.Options;

namespace Kombats.Battle.Infrastructure.Profiles;

/// <summary>
/// Infrastructure implementation of ICombatBalanceProvider.
/// Maps CombatBalanceOptions to Domain CombatBalance.
/// </summary>
public class CombatBalanceProvider : ICombatBalanceProvider
{
    private readonly CombatBalance _balance;

    public CombatBalanceProvider(IOptions<CombatBalanceOptions> options)
    {
        _balance = CombatBalanceMapper.ToDomain(options.Value);
    }

    public CombatBalance GetBalance()
    {
        return _balance;
    }
}


