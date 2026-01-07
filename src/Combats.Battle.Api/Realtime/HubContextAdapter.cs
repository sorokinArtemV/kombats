using Microsoft.AspNetCore.SignalR;

namespace Combats.Battle.Api.Realtime;

/// <summary>
/// Adapter that wraps IHubContext&lt;BattleHub&gt; as IHubContext&lt;Hub&gt;
/// to allow Infrastructure notifier to work without referencing Api.Hubs.BattleHub.
/// </summary>
internal class HubContextAdapter : IHubContext<Hub>
{
    private readonly IHubContext<Hubs.BattleHub> _hubContext;

    public HubContextAdapter(IHubContext<Hubs.BattleHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public IHubClients Clients => _hubContext.Clients;
    public IGroupManager Groups => _hubContext.Groups;
}



