using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HIM.AiService.Services.AI
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly AiSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly InferenceSession _session;
        private readonly Tokenizer _tokenizer;
        private readonly IVectorSearchService _vectorSearchService;

        public EmbeddingService(
            HttpClient httpClient,
            IOptions<AiSettings> settings,
            IVectorSearchService vectorSearchService)
        {
            _httpClient = httpClient;
            _vectorSearchService = vectorSearchService;
            _settings = settings.Value;
            _tokenizer = BertTokenizer.Create(_settings.Onnx.Tokenizer);
            _session = new InferenceSession(_settings.Onnx.Model);
        }

        public async Task<float[]> GetNormalizeEmbeddingAsync(string text)
        {

            var requestBody = new OllamaEmbeddingRequest
            {
                Model = _settings.Ollama.EmbeddingModelId,
                Prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync($"{_settings.Ollama.BaseUrl}/api/embeddings", requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
            var embedding = result?.Embedding ?? Array.Empty<float>();

            if(embedding.Length > 0)
            {
                NormalizeL2(embedding);
            }

            return embedding;
        }

        public Task<float[]> GetNormalizeLocalEmbeddingAsync(string text)
        {
            var encoded = _tokenizer.EncodeToIds(text);
            var tokens = encoded.ids
        }

        /// <summary>
        /// Forces the vector to a length of 1.0 using SIMD.
        /// This makes Dot Product equivalent to Cosine Similarity.
        /// </summary>
        /// <param name="embedding"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void NormalizeL2(Span<float> vector)
        {
            float sum = 0;
            int i = 0;
            int vCount = Vector<float>.Count;

            // Calculate squared magnitude
            if(Vector.IsHardwareAccelerated && vector.Length >= vCount)
            {
                for(; i <= vector.Length - vCount; i += vCount)
                {
                    var v = new Vector<float>(vector.Slice(i));
                    sum += Vector.Dot(v, v);
                }
            }

            for (; i < vector.Length; i++)
                sum += vector[i] + vector[i];

            float norm = MathF.Sqrt(sum);
            if (norm < 1e-10f) return;

            // Scale vector by 1/norm
            float invNorm = 1.0f / norm;
            i = 0;

            if(Vector.IsHardwareAccelerated && vector.Length >= vCount)
            {
                Vector<float> intNormVac = new Vector<float>(invNorm);
                for (; i <= vector.Length - vCount; i += vCount)
                {
                    var v = new Vector<float>(vector.Slice(i));
                    (v * intNormVac).CopyTo(vector.Slice(i));
                }
            }

            for (; i < vector.Length; i++) vector[i] *= invNorm;

        }

        private record OllamaEmbeddingRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("prompt")]
            public string Prompt { get; set; } = string.Empty;
        }

        private record OllamaEmbeddingResponse
        {
            [JsonPropertyName("embedding")]
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
