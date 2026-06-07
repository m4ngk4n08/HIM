using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HIM.AiService.Services.AI
{
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly List<KnowledgeChunks> _chunks = new();
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorSearchService _vectorsearchService;
        private readonly AiSettings _settings;

        public KnowledgeBaseService(
            IEmbeddingService embeddingService,
            IVectorSearchService vectorsearchService,
            IOptions<AiSettings> settings)
        {
            _embeddingService = embeddingService;
            _vectorsearchService = vectorsearchService;
            _settings = settings.Value;
        }

        public async Task InitializeAsync()
        {
            if (_chunks.Any()) return;

            // Load the JSON
            var json = await File.ReadAllTextAsync(_settings.KnowledgeBase.FilePath);
            var kb = JsonDocument.Parse(json);

            // Simple Chunking Strategy: One chunk per logical section

            var rawChunks = FlattenKnowledgeBase(kb.RootElement);

            foreach (var text in rawChunks)
            {
                var vector = await _embeddingService.GetEmbeddingAsync(text);
                _chunks.Add(new KnowledgeChunks { Text  = text , Vector = vector });
            }

        }

        private List<string> FlattenKnowledgeBase(JsonElement rootElement)
        {
            // Convert Json sections into descriptive strings
            var chunks = new List<string>();

            // Process personal information
            if(rootElement.TryGetProperty("personal_info", out var personalInfo))
            {
                chunks.Add($"Personal profile: {personalInfo.GetProperty("name").GetString()} is a {personalInfo.GetProperty("role").GetString()}. Summary: {personalInfo.GetProperty("summary").GetString()}");
                chunks.Add($"Personality and Tone: {personalInfo.GetProperty("personality").GetString()}");
            }

            // Process experience(Contextualize chunks)
            if(rootElement.TryGetProperty("experience", out var experience))
            {
                foreach(var job in experience.EnumerateArray())
                {
                    var company = job.GetProperty("company").GetString();
                    var position = job.GetProperty("position").GetString();
                    var duration = job.GetProperty("duration").GetString();

                    var highligts = string.Join(" ", job.GetProperty("highlights").EnumerateArray().Select(h => h.GetString()));

                    // Create a rich, self-contained sentence so the vector search is highly accurate
                    chunks.Add($"Work experience at {company}: Role: {position} ({duration}). Highlights: {highligts}");
                }
            }

            // Process technical skills
            if(rootElement.TryGetProperty("technical_skills", out var skills))
            {
                foreach(var category in skills.EnumerateObject())
                {
                    var categoryName = category.Name;
                    var skillList = string.Join(" ", category.Value.EnumerateArray().Select(s => s.GetString()));
                    chunks.Add($"Technical Skills - {categoryName}: {skillList}");
                }
            }
            
            // Process Education
            if(rootElement.TryGetProperty("education", out var education))
            {
                chunks.Add($"Education {education.GetProperty("degree").GetString()} from {education.GetProperty("institution").GetString()}, graduated {education.GetProperty("graduation").GetString()}");
            }

            return chunks;
        }

        public async Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3)
        {
            return _chunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Similarity = _vectorsearchService.CalculateCosineSimilarity(queryEmbedding, chunk.Vector)
                })
                .OrderByDescending(x => x.Similarity)
                .Take(topK)
                .Select(j => j.Chunk)
                .ToList();
        }
    }
}
