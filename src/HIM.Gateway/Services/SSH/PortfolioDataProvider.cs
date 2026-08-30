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
        // A relative path is resolved against the app's own directory, not the current working
        // directory - matching GameScoreService and StatsCommandService. In Docker the two are the
        // same (WORKDIR /app), but locally `dotnet run` from the repo root leaves the CWD there,
        // where knowledge-base.json does not exist. An absolute path (what the container sets via
        // KnowledgeBaseSettings__FilePath) is used as given.
        var configuredPath = kbSettings.Value.FilePath;
        var filePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        try
        {
            if (!File.Exists(filePath))
            {
                // Previously a silent return, which surfaced only as "knowledge base not found or
                // corrupted" at the prompt with nothing in the logs to say which path was tried.
                logger.LogWarning("Knowledge base not found at {FilePath}; portfolio commands will be unavailable", filePath);
                return;
            }

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
