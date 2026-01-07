using Combats.Contracts.Battle;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Combats.Battle.Api.Controllers;

/// <summary>
/// DEV-ONLY: Endpoint for creating battles without Matchmaking service.
/// This endpoint is ONLY available in Development environment.
/// In Production, battles are created via Matchmaking service sending CreateBattle command.
/// </summary>
[ApiController]
[Route("dev/battles")]
public class DevBattlesController : ControllerBase
{
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<DevBattlesController> _logger;
    private readonly IWebHostEnvironment _environment;

    public DevBattlesController(
        ISendEndpointProvider sendEndpointProvider,
        ILogger<DevBattlesController> logger,
        IWebHostEnvironment environment)
    {
        _sendEndpointProvider = sendEndpointProvider;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost]
    public async Task<ActionResult<CreateBattleResponse>> CreateBattle([FromBody] CreateBattleRequest request)
    {
        // DEV-ONLY: Check environment
        if (!_environment.IsDevelopment())
        {
            return NotFound(); // Hide endpoint in Production
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid(); // Dev: generate fake MatchId

        var command = new CreateBattle
        {
            BattleId = battleId,
            MatchId = matchId,
            PlayerAId = request.PlayerAId,
            PlayerBId = request.PlayerBId,
            Ruleset = new Ruleset
            {
                Version = 1,
                TurnSeconds = request.TurnSeconds ?? 10,
                NoActionLimit = request.NoActionLimit ?? 3,
                Seed = new Random().Next(),
                HpPerStamina = request.HpPerStamina ?? 10,
                DamagePerStrength = request.DamagePerStrength ?? 2
            },
            RequestedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "DEV: Creating battle via CreateBattle command. BattleId: {BattleId}, PlayerA: {PlayerAId}, PlayerB: {PlayerBId}",
            battleId, request.PlayerAId, request.PlayerBId);

        // Send command via MassTransit Send (point-to-point, same flow as Matchmaking would use)
        var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:battle.create-battle"));
        await sendEndpoint.Send(command);

        return Ok(new CreateBattleResponse
        {
            BattleId = battleId,
            MatchId = matchId
        });
    }
}

public record CreateBattleRequest
{
    public Guid PlayerAId { get; init; }
    public Guid PlayerBId { get; init; }
    public int? TurnSeconds { get; init; }
    public int? NoActionLimit { get; init; }
    public int? HpPerStamina { get; init; }
    public int? DamagePerStrength { get; init; }
}

public record CreateBattleResponse
{
    public Guid BattleId { get; init; }
    public Guid MatchId { get; init; }
}




