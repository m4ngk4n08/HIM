using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace HIM.AiService.Services.AI
{
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        // Distinguishes the header-carrying cache format from the legacy headerless one
        // (whose first 4 bytes are just a chunk count). Never migrate a headerless file.
        private const int CacheMagic = unchecked((int)0xDEC0DE01);
        // Bumped 1 -> 2 for Task 13's FlattenJson rewrite: the source JSON bytes don't change
        // when only the flattening logic changes, so without this the old (wrongly-chunked)
        // cache would keep loading and the fix would silently do nothing.
        private const int CacheSchemaVersion = 2;

        // Chosen well above the largest legitimate consolidated entry in the current knowledge
        // base (~90 words for a stress_test_qna entry) and well below the point where a chunk's
        // real token count (see EmbeddingService.GetNormalizeLocalEmbeddingAsync) risks the
        // ONNX tokenizer's 512-token hard cap. The two project entries (589/650 words) are the
        // only ones that exceed it today, which is exactly what should split, not truncate.
        private const int MaxConsolidatedWords = 200;

        private readonly List<KnowledgeChunks> _chunks = new();
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorSearchService _vectorsearchService;
        private readonly AiSettings _settings;
        private readonly ILogger<KnowledgeBaseService> _logger;

        public KnowledgeBaseService(
            IEmbeddingService embeddingService,
            IVectorSearchService vectorsearchService,
            IOptions<AiSettings> settings,
            ILogger<KnowledgeBaseService> logger)
        {
            _embeddingService = embeddingService;
            _vectorsearchService = vectorsearchService;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_chunks.Any()) return;

            var sourceBytes = await File.ReadAllBytesAsync(_settings.KnowledgeBase.FilePath);
            var sourceHash = ComputeHash(sourceBytes);

            if (await TryLoadCacheAsync(sourceHash))
            {
                _logger.LogInformation(
                    "Knowledge base loaded from cache '{CacheFile}' ({Count} chunks).",
                    _settings.KnowledgeBase.CacheFile, _chunks.Count);
                return;
            }

            _chunks.Clear();

            using var doc = JsonDocument.Parse(sourceBytes);
            var rawChunks = new List<string>();
            FlattenJson(doc.RootElement, string.Empty, rawChunks);

            foreach (var text in rawChunks)
            {
                var vector = await _embeddingService.GetNormalizeLocalEmbeddingAsync(text);
                _chunks.Add(new KnowledgeChunks { Text = text, Vector = vector });
            }

            await SaveCacheAsync(sourceHash);

            _logger.LogInformation(
                "Knowledge base rebuilt from source '{SourceFile}' and cached to '{CacheFile}' ({Count} chunks).",
                _settings.KnowledgeBase.FilePath, _settings.KnowledgeBase.CacheFile, _chunks.Count);
        }

        private static byte[] ComputeHash(byte[] bytes) => SHA256.HashData(bytes);

        /// <summary>
        /// Attempts to load and validate the binary cache against the current source hash.
        /// Returns false (cache miss) on a missing file, a mismatched magic/version/hash,
        /// or any read failure - a corrupt cache must never throw at startup.
        /// </summary>
        private async Task<bool> TryLoadCacheAsync(byte[] expectedHash)
        {
            var cacheFile = _settings.KnowledgeBase.CacheFile;

            if (!File.Exists(cacheFile))
            {
                _logger.LogInformation("No cache file found at '{CacheFile}'; rebuilding.", cacheFile);
                return false;
            }

            try
            {
                using var stream = File.OpenRead(cacheFile);
                using var reader = new BinaryReader(stream);

                var magic = reader.ReadInt32();
                if (magic != CacheMagic)
                {
                    _logger.LogInformation("Cache file '{CacheFile}' has no valid header; rebuilding.", cacheFile);
                    return false;
                }

                var version = reader.ReadInt32();
                if (version != CacheSchemaVersion)
                {
                    _logger.LogInformation(
                        "Cache schema version mismatch (found {Found}, expected {Expected}); rebuilding.",
                        version, CacheSchemaVersion);
                    return false;
                }

                // Check the length against the expected hash size before reading - never
                // trust an attacker/corruption-controlled length as an allocation size.
                var hashLength = reader.ReadInt32();
                if (hashLength != expectedHash.Length)
                {
                    _logger.LogInformation(
                        "Cache hash does not match source '{SourceFile}'; rebuilding.",
                        _settings.KnowledgeBase.FilePath);
                    return false;
                }

                var hash = reader.ReadBytes(hashLength);
                if (!hash.AsSpan().SequenceEqual(expectedHash))
                {
                    _logger.LogInformation(
                        "Cache hash does not match source '{SourceFile}'; rebuilding.",
                        _settings.KnowledgeBase.FilePath);
                    return false;
                }

                var loaded = new List<KnowledgeChunks>();
                var count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var text = reader.ReadString();
                    var vecLen = reader.ReadInt32();
                    var vec = new float[vecLen];
                    for (int j = 0; j < vecLen; j++)
                        vec[j] = reader.ReadSingle();

                    loaded.Add(new KnowledgeChunks { Text = text, Vector = vec });
                }

                _chunks.AddRange(loaded);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache file '{CacheFile}' is corrupt; rebuilding.", cacheFile);
                _chunks.Clear();
                return false;
            }
        }

        private Task SaveCacheAsync(byte[] sourceHash)
        {
            var cacheFile = _settings.KnowledgeBase.CacheFile;
            var cacheDir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(cacheDir))
                Directory.CreateDirectory(cacheDir);

            // Write to a temp file and rename into place so a kill mid-write can never
            // leave a truncated cache file behind (a truncated file rebuilds cleanly anyway,
            // but there is no reason to leave one around).
            var tempFile = cacheFile + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = File.Create(tempFile))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(CacheMagic);
                    writer.Write(CacheSchemaVersion);
                    writer.Write(sourceHash.Length);
                    writer.Write(sourceHash);

                    writer.Write(_chunks.Count);
                    foreach (var chunk in _chunks)
                    {
                        writer.Write(chunk.Text);
                        writer.Write(chunk.Vector.Length);
                        foreach (var val in chunk.Vector)
                            writer.Write(val);
                    }
                }

                File.Move(tempFile, cacheFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }

            return Task.CompletedTask;
        }

        private void FlattenJson(JsonElement element, string prefix, List<string> chunks)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    FlattenObject(element, prefix, chunks);
                    break;

                case JsonValueKind.Array:
                    var items = element.EnumerateArray().ToList();
                    if (!items.Any()) break;

                    // 1. If it's a simple list (like strings), join them.
                    if (items.All(x => x.ValueKind != JsonValueKind.Object && x.ValueKind != JsonValueKind.Array))
                    {
                        chunks.Add($"{prefix}: {string.Join(", ", items.Select(x => x.ToString()))}");
                    }
                    // 2. If it's a list of OBJECTS (like Experience or Projects), consolidate each object -
                    // or split it by field if consolidating would risk truncation (see FlattenObject).
                    else
                    {
                        foreach (var item in items)
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                // Tag each item's chunks with its own identity (e.g. a project's
                                // "name") so a split-by-field entry stays attributable to the right
                                // item - every item in this array otherwise shares the same prefix.
                                var itemPrefix = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                    ? $"{prefix} [{nameProp.GetString()}]"
                                    : prefix;
                                FlattenObject(item, itemPrefix, chunks);
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

        /// <summary>
        /// Consolidates an object into one high-context sentence (the original behavior) when
        /// that sentence stays comfortably under <see cref="MaxConsolidatedWords"/>. Otherwise
        /// splits it into one chunk per top-level field instead, so a large entry (e.g. a project
        /// with nested architecture/achievements/security sections) never becomes a single chunk
        /// long enough for EmbeddingService's 512-token cap to silently truncate the back half.
        /// </summary>
        private void FlattenObject(JsonElement obj, string prefix, List<string> chunks)
        {
            var candidate = ConsolidateObject(obj);
            if (CountWords(candidate) <= MaxConsolidatedWords)
            {
                chunks.Add($"{prefix}: {candidate}");
                return;
            }

            foreach (var prop in obj.EnumerateObject())
            {
                var fieldPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix} {prop.Name}";
                FlattenJson(prop.Value, fieldPrefix, chunks);
            }
        }

        private static string ConsolidateObject(JsonElement obj)
        {
            var parts = new List<string>();
            foreach (var prop in obj.EnumerateObject())
            {
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.Array:
                        var subItems = string.Join("; ", prop.Value.EnumerateArray().Select(v =>
                            v.ValueKind == JsonValueKind.Object ? ConsolidateObject(v) : v.ToString()));
                        parts.Add($"{prop.Name}: {subItems}");
                        break;
                    case JsonValueKind.Object:
                        parts.Add($"{prop.Name}: {ConsolidateObject(prop.Value)}");
                        break;
                    default:
                        parts.Add($"{prop.Name}: {prop.Value}");
                        break;
                }
            }
            return string.Join(". ", parts);
        }

        private static int CountWords(string s) =>
            s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        public async Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3, float minScore = float.NegativeInfinity)
        {
            if (!_chunks.Any())
            {
                await InitializeAsync();
            }

            if (!_chunks.Any()) return new List<KnowledgeChunks>();

            // Use PriorityQueue for O(N log k)
            var pq = new PriorityQueue<(KnowledgeChunks Chunk, float Score), float>();

            foreach (var chunk in _chunks)
            {
                float similarity = _vectorsearchService.CalculateDotProduct(queryEmbedding, chunk.Vector);
                pq.Enqueue((chunk, similarity), similarity); // Min-heap: the smallest score is the one Dequeue drops below

                if (pq.Count > topK) pq.Dequeue();
            }

            // topK is only an upper bound: a chunk below minScore is dropped even if it made
            // the top-K cut, so an irrelevant match never rides along just to fill the quota.
            var results = new List<KnowledgeChunks>();
            while (pq.Count > 0)
            {
                var (chunk, score) = pq.Dequeue();
                if (score >= minScore) results.Insert(0, chunk);
            }
            return results;
        }
    }
}
