using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

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

        public async Task<string> AskAsync(string question)
        {
            // Get embedding for the question
            var queryVector = await _embeddingService.GetEmbeddingAsync(question);
            // Search for relevant chunks
            var chunks = await _kbService.SearchAsync(queryVector);
            var context = string.Join("\n", chunks.Select(c => c.Text));

            // Synthesize answer using the Kernel(Semantic kernel)
            var prompt = $@"
                You are Angelo's AI Portfolio Assistant. Use the following context to answer the user's question.
                if the answer isn't in the context, be honest and say you don't know, but offer to provide his contact info.
                
                Maintain a professional, technical, yet witty 'Gen Z' tone.

                Context:
                {context}

                User Question: {question}
                Answer:
        
            ";

            var result = await _kernel.InvokePromptAsync<string>(prompt);

            return result.ToString() ?? "I'm sorry, I couldn't process that.";
        }

    }
}
