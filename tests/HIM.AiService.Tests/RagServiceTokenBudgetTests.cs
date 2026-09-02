using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14E (SEC-04): once the daily token ceiling is spent, AskAsync must degrade gracefully -
/// a static answer built from retrieval, never an error or a call to the (here-unreachable,
/// since these tests use a fake API key) chat model.
/// </summary>
public class RagServiceTokenBudgetTests
{
    private const string ContextText = "personal_info: summary: Angelo builds RAG pipelines in C#.";

    private static RagService CreateService(DailyTokenBudgetTracker tracker)
    {
        var settings = new AiSettings
        {
            ChatProvider = "Gemini",
            Gemini = new GeminiSettings { ModelId = "gemini-3.1-flash-lite", ApiKey = "test-key" },
            Security = new SecuritySettings { MaxQuestionLength = 500 },
            KnowledgeBase = new KnowledgeBaseSettings { MinSimilarityScore = 0.3f }
        };

        var embedding = new Mock<IEmbeddingService>();
        embedding.Setup(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>())).ReturnsAsync(new float[384]);

        var kb = new Mock<IKnowledgeBaseService>();
        kb.Setup(m => m.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>()))
            .ReturnsAsync(new List<KnowledgeChunks> { new() { Text = ContextText, Vector = new float[384] } });

        return new RagService(embedding.Object, kb.Object, tracker, Options.Create(settings), NullLogger<RagService>.Instance);
    }

    private static async Task<string> AskAsync(RagService service, string question)
    {
        var parts = new List<string>();
        await foreach (var part in service.AskAsync(question))
            parts.Add(part);
        return string.Concat(parts);
    }

    [Fact]
    public async Task AskAsync_WhenBudgetExhausted_RepliesWithAStaticAnswer_BuiltFromRetrievedContext()
    {
        var settings = Options.Create(new AiSettings { TokenBudget = new TokenBudgetSettings { DailyTokenCeiling = 1 } });
        var tracker = new DailyTokenBudgetTracker(settings);
        tracker.RecordUsage(1); // exhaust it immediately

        var reply = await AskAsync(CreateService(tracker), "What does Angelo build?");

        Assert.Contains("static knowledge base", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ContextText, reply);
    }

    [Fact]
    public async Task AskAsync_WhenBudgetExhausted_NeverReachesTheChatModel()
    {
        // With a real (fake) Gemini API key wired up, an actual model call here would throw a
        // network/auth exception - AskAsync must never get that far when the budget is spent.
        var settings = Options.Create(new AiSettings { TokenBudget = new TokenBudgetSettings { DailyTokenCeiling = 1 } });
        var tracker = new DailyTokenBudgetTracker(settings);
        tracker.RecordUsage(1);

        var exception = await Record.ExceptionAsync(() => AskAsync(CreateService(tracker), "What does Angelo build?"));

        Assert.Null(exception);
    }
}
