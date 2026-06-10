using HIM.AiService.Models;
using HIM.AiService.Services.AI.Interface;
using Microsoft.AspNetCore.Mvc;

namespace HIM.AiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IRagService _ragService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            IRagService ragService,
            ILogger<ChatController> logger)
        {
            _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Refreshes and re-indexes the knoweledge base vectors.
        /// </summary>
        [HttpPost("initialize")]
        public async Task<IActionResult> Initialize()
        {
            try
            {
                _logger.LogInformation("Initializing knoweldge base..");
                
                await _ragService.InitializeAsync();

                return Ok(new { Message = "Knowledge base indexed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize knowledge base");
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Process a portfolio inquiry and streams the AI's response in real-time
        /// </summary>
        /// <param name="request">The user's question.</param>
        /// <param name="ct">Signals if the client has disconnected.</param>
        [HttpPost("ask")]
        public IAsyncEnumerable<string> Ask([FromBody] ChatRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                _logger.LogWarning("Received and empty chat request.");
                return AsyncEnumerable.Empty<string>();
            }

            _logger.LogInformation("Synthesizing answer for inquiry: {Inquiry}", request.Question);

            return _ragService.AskAsync(request.Question, ct);

        }

    }
}
