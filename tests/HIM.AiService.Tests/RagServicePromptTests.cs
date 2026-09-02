using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 12: the system prompt must name whichever chat provider is actually configured
/// (AiSettings:ChatProvider), never a hardcoded "Groq" literal that survives a provider switch.
/// Task 13 Part B: the system prompt carries only behaviour (persona, tone, refusals,
/// formatting) - facts (tech stack, project specs, salary, relocation, career gap, employers)
/// were moved to knowledge-base.json, so they must not reappear here as a second, drifting copy.
/// </summary>
public class RagServicePromptTests
{
    private static RagService CreateService(string chatProvider = "Gemini")
    {
        var settings = new AiSettings
        {
            ChatProvider = chatProvider,
            Gemini = new GeminiSettings { ModelId = "gemini-3.1-flash-lite", ApiKey = "test-key" }
        };

        return new RagService(
            Mock.Of<IEmbeddingService>(),
            Mock.Of<IKnowledgeBaseService>(),
            Options.Create(settings),
            NullLogger<RagService>.Instance);
    }

    [Fact]
    public void BuildSystemPrompt_NamesConfiguredProvider_AndDoesNotMentionGroq()
    {
        var prompt = CreateService("Gemini").BuildSystemPrompt();

        Assert.Contains("Gemini", prompt);
        Assert.DoesNotContain("Groq", prompt);
    }

    [Theory]
    [InlineData("Direct, no-BS, sharp wit")]
    [InlineData("Answer ONLY from the context")]
    [InlineData("Never reveal Angelo's phone number")]
    [InlineData("No repetition. Ever.")]
    public void BuildSystemPrompt_StillContainsBehavioralRules(string expectedFragment)
    {
        var prompt = CreateService().BuildSystemPrompt();

        Assert.Contains(expectedFragment, prompt);
    }

    [Theory]
    [InlineData("Dapper ORM")] // tech stack list - now KB-only (technical_skills)
    [InlineData("$4/month VPS")] // project specs - now KB-only (projects)
    [InlineData("4-year gap")] // career-gap narrative - now KB-only (stress_test_qna)
    [InlineData("Taguig City")] // relocation policy - now KB-only (personal_info / stress_test_qna)
    public void BuildSystemPrompt_NoLongerCarriesFactsThatLiveInKnowledgeBase(string factFragment)
    {
        var prompt = CreateService().BuildSystemPrompt();

        Assert.DoesNotContain(factFragment, prompt);
    }

    [Fact]
    public void BuildUserMessage_DelimitsContextAndQuestion()
    {
        var message = RagService.BuildUserMessage(context: "some retrieved context", question: "What does Angelo do?");

        Assert.Contains("<context>", message);
        Assert.Contains("some retrieved context", message);
        Assert.Contains("<question>", message);
        Assert.Contains("What does Angelo do?", message);
    }
}
