using Errandy.Api.Extensions;
using Errandy.Application.Features.Chat.GetMessages;
using Errandy.Application.Features.Chat.SendMessage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Errandy.Api.Filters;
namespace Errandy.Api.Controllers;

/// <summary>One chat thread per errand (PRD "Communication System").</summary>
[ApiController]
[Route("api/errands/{errandId:guid}/messages")]
[Authorize]
[ConflictExceptionFilter]
public class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(Guid errandId, CancellationToken cancellationToken)
    {
        var messages = await _mediator.Send(
            new GetMessagesQuery { ErrandId = errandId, RequestingUserId = User.GetUserId() },
            cancellationToken);

        return Ok(messages);
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(Guid errandId, [FromBody] SendMessageRequestBody body, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SendMessageCommand { ErrandId = errandId, SenderId = User.GetUserId(), Content = body.Content },
            cancellationToken);

        return CreatedAtAction(
            nameof(GetMessages),
            new { errandId },
            new { id = result.MessageId, contentWasMasked = result.ContentWasMasked });
    }
}

public record SendMessageRequestBody(string Content);