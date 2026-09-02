using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14C (SEC-06): a retrieval-time exception used to reach the visitor as
/// "AI Service: Knowledge retrieval failed: {ex.Message}" - the exact class of leak SEC-06 is
/// about, just one layer below the HTTP pipeline ErrorHandlingMiddleware covers. The real detail
/// must go to the logger only; the visitor gets a generic, persona-toned line.
/// </summary>
public class RagServiceRetrievalErrorTests
{
    [Fact]
    public async Task AskAsync_WhenRetrievalThrows_RepliesGenerically_AndLeaksNoExceptionText()
    {
        var settings = new AiSettings
        {
            ChatProvider = "Gemini",
            Gemini = new GeminiSettings { ModelId = "gemini-3.1-flash-lite", ApiKey = "test-key" },
            Security = new SecuritySettings { MaxQuestionLength = 500 },
            KnowledgeBase = new KnowledgeBaseSettings { MinSimilarityScore = 0.3f }
        };

        var embedding = new Mock<IEmbeddingService>();
        embedding
            .Setup(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("db=postgres://admin:hunter2@internal-host/prod"));

        var kb = new Mock<IKnowledgeBaseService>();

        var service = new RagService(
            embedding.Object,
            kb.Object,
            Options.Create(settings),
            NullLogger<RagService>.Instance);

        var parts = new List<string>();
        await foreach (var part in service.AskAsync("What does Angelo work with?"))
            parts.Add(part);
        var reply = string.Concat(parts);

        Assert.DoesNotContain("hunter2", reply);
        Assert.DoesNotContain("InvalidOperationException", reply);
        Assert.DoesNotContain("Knowledge retrieval failed", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("angelodavales0528@gmail.com", reply);
    }
}
