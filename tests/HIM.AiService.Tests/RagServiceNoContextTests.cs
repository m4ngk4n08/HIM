using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 13 follow-up: KnowledgeBaseSettings.MinSimilarityScore made the empty-retrieval path
/// reachable for the first time - before it, topK always returned chunks, so "no relevant
/// context" only happened with an empty knowledge base. An off-topic question must get the
/// persona's fallback line, never the internal diagnostic that path used to emit.
/// </summary>
public class RagServiceNoContextTests
{
    private static RagService CreateServiceReturningNoChunks()
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
            .ReturnsAsync(new float[384]);

        // Everything scored below MinSimilarityScore, so the cutoff drops it all.
        var kb = new Mock<IKnowledgeBaseService>();
        kb
            .Setup(m => m.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>()))
            .ReturnsAsync(new List<KnowledgeChunks>());

        return new RagService(
            embedding.Object,
            kb.Object,
            new DailyTokenBudgetTracker(Options.Create(settings)),
            Options.Create(settings),
            NullLogger<RagService>.Instance);
    }

    private static async Task<string> AskAsync(RagService service, string question)
    {
        var parts = new List<string>();
        await foreach (var part in service.AskAsync(question))
            parts.Add(part);
        return string.Concat(parts);
    }

    [Fact]
    public async Task AskAsync_WithNothingAboveTheThreshold_RepliesWithThePersonaFallback()
    {
        var reply = await AskAsync(CreateServiceReturningNoChunks(), "What is the weather in Tokyo today?");

        Assert.Contains("not in my knowledge base", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("angelodavales0528@gmail.com", reply);
    }

    [Fact]
    public async Task AskAsync_WithNothingAboveTheThreshold_LeaksNoInternalDiagnostic()
    {
        var reply = await AskAsync(CreateServiceReturningNoChunks(), "What is the weather in Tokyo today?");

        // The old wording - a developer-facing string that reached the visitor verbatim.
        Assert.DoesNotContain("AI Service:", reply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No relevant context found", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemPrompt_TellsTheModelTheSameFallbackWording_SoTheTwoCannotDrift()
    {
        var prompt = CreateServiceReturningNoChunks().BuildSystemPrompt();

        Assert.Contains("That's not in my knowledge base", prompt);
    }
}
