using AIAssistantAPI.Interfaces;
using AIAssistantAPI.Model;
using Microsoft.AspNetCore.Mvc;


[ApiController]
[Route("[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost("process-message")]
    public async Task<IActionResult> ProcessChatMessage([FromBody] ChatRequest chatRequest, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid model state for ChatRequest");
            return BadRequest(ModelState);
        }

        string sessionId = chatRequest.SessionId;

        try
        {            
            _logger.LogInformation("Processing chat message for session {SessionId}", sessionId);

            var message = await _chatService.ProcessChatMessageAsync(sessionId, chatRequest.Message, cancellationToken);
            var response = new ChatResponse { Message = message };

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat message processing was canceled for session {SessionId}", sessionId);
            return BadRequest("Request was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing chat message for session {SessionId}", sessionId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("end-thread")]
    public async Task<IActionResult> EndThread([FromBody] string sessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ending chat session {SessionId}", sessionId);

        await _chatService.EndChatSessionAsync(sessionId, cancellationToken);
        return Ok("Chat session ended.");
    }
}
