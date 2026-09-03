using HIM.Gateway.Models;
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

            httpRequest.Headers.Add("X-Ai-Shared-Secret", _settings.SharedSecret);

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

        public async Task<(CitationResult? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct, string? correlationId = null)
        {
            var request = new { Question = question };

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_settings.BaseUrl}/api/chat/cite")
            {
                Content = JsonContent.Create(request)
            };

            if (!string.IsNullOrEmpty(correlationId))
                httpRequest.Headers.Add("X-Request-Id", correlationId);

            httpRequest.Headers.Add("X-Ai-Shared-Secret", _settings.SharedSecret);

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                // ChatController.Cite's rejection body is a JSON-encoded string (BadRequest with
                // a string argument), not an object - read it as one so the visitor sees the
                // actual reason (e.g. the MaxQuestionLength message) instead of a raw status code.
                string? errorText = null;
                try
                {
                    errorText = await response.Content.ReadFromJsonAsync<string>(cancellationToken: ct);
                }
                catch
                {
                    // Body wasn't a JSON string (or there was no body) - fall through to the
                    // generic message below.
                }

                return (null, errorText ?? $"Gateway Error: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<CitationResult>(cancellationToken: ct);
            return (result, null);
        }
    }
}
