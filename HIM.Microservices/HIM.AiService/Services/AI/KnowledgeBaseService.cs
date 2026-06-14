using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Xml.Linq;

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

            // Try load from Binary Cache(instant)
            if(File.Exists(_settings.KnowledgeBase.CacheFile))
            {
                await LoadCacheAsync();
                return;
            }

            // Cold Start: Process JSON
            var json = await File.ReadAllTextAsync(_settings.KnowledgeBase.FilePath);
            using var doc = JsonDocument.Parse(json);
            var rawChunks = new List<string>();
            FlattenJson(doc.RootElement, string.Empty, rawChunks);

            foreach(var text in rawChunks)
            {
                var vector = await _embeddingService.GetNormalizeLocalEmbeddingAsync(text);
                _chunks.Add(new KnowledgeChunks { Text = text, Vector = vector });
            }

            // Save for next time
            await SaveCacheAsync();

        }

        private async Task SaveCacheAsync()
        {
            using var stream = File.Create(_settings.KnowledgeBase.CacheFile);
            using var writer = new BinaryWriter(stream);
            writer.Write(_chunks.Count);
            foreach(var chunk in _chunks)
            {
                writer.Write(chunk.Text);
                writer.Write(chunk.Vector.Length);
                foreach (var val in chunk.Vector)
                    writer.Write(val);
            }
        }

        private void FlattenJson(JsonElement element, string prefix, List<string> chunks)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                    {
                        string newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix} {prop.Name}";
                        FlattenJson(prop.Value, newPrefix, chunks);
                    }
                    break;

                case JsonValueKind.Array:
                    var items = element.EnumerateArray().ToList();
                    if (!items.Any()) break;

                    // 1. If it's a simple list (like strings), join them.
                    if (items.All(x => x.ValueKind != JsonValueKind.Object && x.ValueKind != JsonValueKind.Array))
                    {
                        chunks.Add($"{prefix}: {string.Join(", ", items.Select(x => x.ToString()))}");
                    }
                    // 2. If it's a list of OBJECTS (like Experience or Projects), consolidate each object!
                    else
                    {
                        foreach (var item in items)
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                // CONSOLIDATION LOGIC:
                                // Turn the whole job entry into one single high-context sentence.
                                var builder = new List<string>();
                                foreach (var prop in item.EnumerateObject())
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        var subItems = string.Join("; ", prop.Value.EnumerateArray().Select(v => v.ToString()));
                                        builder.Add($"{prop.Name}: {subItems}");
                                    }
                                    else
                                    {
                                        builder.Add($"{prop.Name}: {prop.Value}");
                                    }
                                }
                                chunks.Add($"{prefix}: {string.Join(". ", builder)}");
                            }
                            else
                            {
                                FlattenJson(item, prefix, chunks);
                            }
                        }
                    }
                    break;

                default:
                    string val = element.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) chunks.Add($"{prefix}: {val}");
                    break;
            }
        }

        private async Task LoadCacheAsync()
        {
            using var stream = File.OpenRead(_settings.KnowledgeBase.CacheFile);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            for(int i = 0; i < count; i++)
            {
                var text = reader.ReadString();
                int vecLen = reader.ReadInt32();
                var vec = new float[vecLen];
                for (int j = 0; j < vecLen; j++)
                    vec[j] = reader.ReadSingle();

                _chunks.Add(new KnowledgeChunks { Text = text, Vector = vec });
            }
        }

        public async Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3)
        {
            if (!_chunks.Any())
            {
                await InitializeAsync();
            }

            if (!_chunks.Any()) return new List<KnowledgeChunks>();

            // Use PriorityQueue for O(N log k)
            var pq = new PriorityQueue<KnowledgeChunks, float>();

            foreach (var chunk in _chunks)
            {
                float similarity = _vectorsearchService.CalculateCosineSimilarity(queryEmbedding, chunk.Vector);
                pq.Enqueue(chunk, similarity); // Negative because PriorityQueue is a Min-Heap by default

                if (pq.Count > topK) pq.Dequeue();
            }

            var results = new List<KnowledgeChunks>();
            while (pq.Count > 0) results.Insert(0, pq.Dequeue());
            return results;
        }
    }
}
