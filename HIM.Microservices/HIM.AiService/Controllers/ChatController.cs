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
            _ragService = ragService;
            _logger = logger;
        }

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

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return BadRequest("Question cannot be empty.");

                var answer = await _ragService.AskAsync(request.Question);
                return Ok(new { Answer = answer });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

    }
}
