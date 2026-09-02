using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 13 Part C / SEC-05: a question longer than AiSettings:Security:MaxQuestionLength must
/// be rejected before it ever reaches retrieval or the model - never truncated into the prompt.
/// </summary>
public class RagServiceInputCapTests
{
    private static RagService CreateService(int maxQuestionLength, out Mock<IEmbeddingService> embeddingMock)
    {
        var settings = new AiSettings
        {
            ChatProvider = "Gemini",
            Gemini = new GeminiSettings { ModelId = "gemini-3.1-flash-lite", ApiKey = "test-key" },
            Security = new SecuritySettings { MaxQuestionLength = maxQuestionLength }
        };

        embeddingMock = new Mock<IEmbeddingService>();

        return new RagService(
            embeddingMock.Object,
            Mock.Of<IKnowledgeBaseService>(),
            Options.Create(settings),
            NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task AskAsync_RejectsOverLongQuestion_WithoutTouchingRetrieval()
    {
        var service = CreateService(maxQuestionLength: 20, out var embeddingMock);
        var overLongQuestion = new string('a', 21);

        var chunks = new List<string>();
        await foreach (var chunk in service.AskAsync(overLongQuestion))
            chunks.Add(chunk);

        var reply = string.Concat(chunks);
        Assert.Contains("too long", reply, StringComparison.OrdinalIgnoreCase);
        embeddingMock.Verify(m => m.GetNormalizeLocalEmbeddingAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_NeverIncludesTheRawOverLongQuestionInItsReply()
    {
        var service = CreateService(maxQuestionLength: 10, out _);
        var overLongQuestion = "this-question-is-definitely-longer-than-the-cap-allows";

        var chunks = new List<string>();
        await foreach (var chunk in service.AskAsync(overLongQuestion))
            chunks.Add(chunk);

        var reply = string.Concat(chunks);
        Assert.DoesNotContain(overLongQuestion, reply);
    }
}
