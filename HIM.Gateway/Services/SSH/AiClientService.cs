using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    public class AiClientService : IAiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly AiServiceSettings _settings;

        public AiClientService(
            HttpClient httpClient,
            IOptions<AiServiceSettings> settings)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
        }
        public async IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null)
        {
            // Prepare the request
            var request = new { Question = question };

            // Call the AI Microservice
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.BaseUrl}/api/chat/ask")
            {
                Content = JsonContent.Create(request)
            };

            if (!string.IsNullOrEmpty(correlationId))
                httpRequest.Headers.Add("X-Request-Id", correlationId);

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                yield return $"Gateway Error: {response.StatusCode}";
                yield break;
            }

            // Read the stream of chunks
            var stream = response.Content.ReadFromJsonAsAsyncEnumerable<string>(cancellationToken: ct);

            if (stream == null) yield break;

            await foreach(var chunk in stream.WithCancellation(ct))
            {
                if (chunk != null) yield return chunk;
            }

        }
    }
}
