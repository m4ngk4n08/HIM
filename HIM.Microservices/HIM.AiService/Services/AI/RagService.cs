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
                yield return $"[AI Service] {error}";
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
            try
            {
                return $@"
                    You are Angelo's AI Portfolio Assistant. Use the following context to answer the user's question.
                    if the answer isn't in the context, be honest and say you don't know, but offer to provide his contact info.
                
                    Maintain a professional, technical, yet witty 'Gen Z' tone. Prioritize users readability use bullet points highligts etc. .

                    Context:
                    {context}

                    User Question: {question}
                    Answer:
        
                ";
            }
            catch (Exception)
            {

                throw;
            }
        }

        private async Task<(string? context, string? error)> TryGetContextAsync(string question)
        {
            try
            {
                var queryVector = await _embeddingService.GetEmbeddingAsync(question);
                var chunks = await _kbService.SearchAsync(queryVector);

                return (string.Join("\n", chunks.Select(j => j.Text)), null);
            }
            catch (Exception ex)
            {
                return (null, $"Knowledge retrieval failed: {ex.Message}");
            }
        }
    }
}
