using System;
using System.IO;
using System.Text.Json;
using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Services.SSH;

public class PortfolioDataProvider : IPortfolioDataProvider
{
    public PortfolioData? Data { get; }

    public PortfolioDataProvider(IOptions<KnowledgeBaseSettings> kbSettings, ILogger<PortfolioDataProvider> logger)
    {
        var filePath = kbSettings.Value.FilePath;

        try
        {
            if (!File.Exists(filePath)) return;

            var json = File.ReadAllText(filePath);
            Data = JsonSerializer.Deserialize<PortfolioData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load knowledge base from {FilePath}", filePath);
        }
    }
}
