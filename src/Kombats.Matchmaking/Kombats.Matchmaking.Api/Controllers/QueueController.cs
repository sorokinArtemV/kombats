using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases;
using Kombats.Matchmaking.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Kombats.Matchmaking.Api.Controllers;

[ApiController]
[Route("queue")]
public class QueueController : ControllerBase
{
    private readonly QueueService _queueService;
    private readonly ILogger<QueueController> _logger;

    public QueueController(
        QueueService queueService,
        ILogger<QueueController> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    /// <summary>
    /// POST /queue/join
    /// Joins a player to the matchmaking queue.
    /// </summary>
    [HttpPost("join")]
    public async Task<ActionResult<QueueStatusResponse>> JoinQueue([FromBody] JoinQueueRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var variant = request.Variant ?? "default";
        var status = await _queueService.JoinQueueAsync(request.PlayerId, variant, cancellationToken);

        return Ok(QueueStatusResponse.FromStatus(status));
    }

    /// <summary>
    /// POST /queue/leave
    /// Removes a player from the matchmaking queue.
    /// </summary>
    [HttpPost("leave")]
    public async Task<ActionResult> LeaveQueue([FromBody] LeaveQueueRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var variant = request.Variant ?? "default";
        var result = await _queueService.LeaveQueueAsync(request.PlayerId, variant, cancellationToken);

        return result.Type switch
        {
            LeaveQueueResultType.LeftSuccessfully => Ok(new { Searching = false }),
            LeaveQueueResultType.NotInQueue => Ok(new { Searching = false }),
            LeaveQueueResultType.AlreadyMatched => Conflict(new
            {
                Searching = false,
                MatchId = result.MatchInfo!.MatchId,
                BattleId = result.MatchInfo!.BattleId
            }),
            _ => StatusCode(500, "Unexpected result type")
        };
    }

    /// <summary>
    /// GET /queue/status?playerId={guid}
    /// Gets the current match status for a player.
    /// Returns: Searching (if queued) or Matched with match state (Created/BattleCreateRequested/BattleCreated/Completed).
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<QueueStatusResponse>> GetStatus([FromQuery] Guid playerId, CancellationToken cancellationToken)
    {
        var status = await _queueService.GetStatusAsync(playerId, cancellationToken);

        if (status == null)
        {
            return Ok(new QueueStatusResponse
            {
                Status = "NotQueued"
            });
        }

        return Ok(QueueStatusResponse.FromStatus(status));
    }
}

public class JoinQueueRequest
{
    public required Guid PlayerId { get; init; }
    public string? Variant { get; init; }
}

public class LeaveQueueRequest
{
    public required Guid PlayerId { get; init; }
    public string? Variant { get; init; }
}

public class QueueStatusResponse
{
    public required string Status { get; init; }
    public Guid? MatchId { get; init; }
    public Guid? BattleId { get; init; }
    public string? MatchState { get; init; }

    public static QueueStatusResponse FromStatus(PlayerMatchStatus status)
    {
        return status.State switch
        {
            PlayerMatchState.Searching => new QueueStatusResponse
            {
                Status = "Searching"
            },
            PlayerMatchState.Matched => new QueueStatusResponse
            {
                Status = "Matched",
                MatchId = status.MatchId,
                BattleId = status.BattleId,
                MatchState = status.MatchState?.ToString()
            },
            _ => throw new InvalidOperationException($"Unknown player match state: {status.State}")
        };
    }
}

