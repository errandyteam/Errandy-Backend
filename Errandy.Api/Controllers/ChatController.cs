using Errandy.Application.Features.Chat.GetMessages;
using Errandy.Application.Features.Chat.SendMessage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Errandy.Api.Controllers;

/// <summary>
/// One chat thread per errand (PRD "Communication System"). Phone numbers
/// stay masked — this endpoint is the only sanctioned communication channel
/// between customer and runner while an errand is active.
/// </summary>
[ApiController]
[Route("api/errands/{errandId:guid}/messages")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(Guid errandId, [FromQuery] Guid requestingUserId, CancellationToken cancellationToken)
    {
        var messages = await _mediator.Send(
            new GetMessagesQuery { ErrandId = errandId, RequestingUserId = requestingUserId },
            cancellationToken);
        return Ok(messages);
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(Guid errandId, [FromBody] SendMessageRequestBody body, CancellationToken cancellationToken)
    {
        var messageId = await _mediator.Send(
            new SendMessageCommand { ErrandId = errandId, SenderId = body.SenderId, Content = body.Content },
            cancellationToken);
        return CreatedAtAction(nameof(GetMessages), new { errandId }, new { id = messageId });
    }
}

/// <summary>
/// Once ICurrentUserService is wired in, SenderId/RequestingUserId should
/// come from the authenticated user's claims, not the request — placeholder for now.
/// </summary>
public record SendMessageRequestBody(Guid SenderId, string Content);
