using ARC.Api.Auth;
using ARC.Api.Chat;
using Microsoft.AspNetCore.Mvc;

namespace ARC.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly BusinessChatService _chat;

    public ChatController(BusinessChatService chat) => _chat = chat;

    [HttpPost]
    public async Task<ActionResult<BusinessChatApiResponse>> PostAsync(
        [FromBody] BusinessChatApiRequest request,
        CancellationToken cancellationToken)
    {
        var actor = ArcActorHttp.GetRequired(HttpContext);
        var correlationId = HttpContext.TraceIdentifier;
        var response = await _chat.HandleAsync(request, actor, correlationId, cancellationToken);
        return Ok(response);
    }
}
