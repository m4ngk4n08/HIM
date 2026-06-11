using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using System.Runtime.CompilerServices;

namespace HIM.AiService.Services.AI
{
    public class RagService : IRagService
    {
        private readonly Kernel _kernel;
        private readonly IEmbeddingService _embeddingService;
        private readonly IKnowledgeBaseService _kbService;

        public RagService(
            IEmbeddingService embeddingService,
            IKnowledgeBaseService kbService,
            IOptions<AiSettings> settings)
        {
            _embeddingService = embeddingService;
            _kbService = kbService;

            var builder = Kernel.CreateBuilder();
            builder.AddOllamaChatCompletion(settings.Value.Ollama.ModelId, new Uri(settings.Value.Ollama.BaseUrl));

            _kernel = builder.Build();
        }

        public async Task InitializeAsync()
        {
            await _kbService.InitializeAsync();
        }

        public async IAsyncEnumerable<string> AskAsync(string question, [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Setup phase(using a tuple-based result pattern for clean error handling)
            var (context, error) = await TryGetContextAsync(question);

            if(error != null)
            {
                yield return $"AI Service: {error}";
                yield break;
            }

            // Synthesize phase (Direct streaming)
            var prompt = BuildPrompt(context!, question);
            var stream = _kernel.InvokePromptStreamingAsync(prompt, cancellationToken: ct);

            await foreach(var chunk in stream.WithCancellation(ct))
            {
                var content = chunk.ToString();

                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }

        private string BuildPrompt(string context, string question)
        {
            return $"""
                    You are Angelo's AI Portfolio Assistant. I built you to help people understand my skills and journey – no BS, no fluff.

                    Context is below. If the answer isn't there, just say so. Don't make stuff up.

                    **Tone rules (follow them exactly):**
                    - Professional, but I've got a sharp, witty edge. Think "senior dev who's seen some shit and still shows up."
                    - Use bullet points and bold text when it helps. Don't overdo it.
                    - Keep answers tight. One paragraph max unless the question needs more.
                    - If someone asks about my career gaps: Be direct. "Angelo took time off after a personal loss. He kept coding on the side, built a RAG system, and is now ready to work."
                    - If they ask about my skills: .NET 8, Angular, EF Core, PostgreSQL, OpenAI, Docker – I know my stack.
                    - If they ask why I'm not a pure frontend or backend: I'm full-stack, but I lean backend because quiet focus is where I thrive.
                    - If they ask about my RAG project: It's a live, deployed Intelligent Search system. I built it to prove I can still ship.

                    **Context:**
                    {context}

                    **User question:**
                    {question}

                    **Your answer:**
                    """;
        }

        private async Task<(string? context, string? error)> TryGetContextAsync(string question)
        {
            try
            {

                // CRITICAL: we must normalize the query vector to match our knowledge base vectors
                var queryVector = await _embeddingService.GetNormalizeEmbeddingAsync(question);

                // Optimize Search using SIMD Dot Product + PriorityQueue
                var chunks = await _kbService.SearchAsync(queryVector, topK: 10);

                if (!chunks.Any())
                    return (null, "No relevant context found in the knowledge base.");

                var contextBody = string.Join("\n---\n", chunks.Select(j => j.Text));
                return (contextBody, null);
            }
            catch (Exception ex)
            {
                return (null, $"Knowledge retrieval failed: {ex.Message}");
            }
        }
    }
}
