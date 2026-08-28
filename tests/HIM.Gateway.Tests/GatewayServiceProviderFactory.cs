using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Tests;

/// <summary>
/// Builds the same service graph as HIM.Gateway/Program.cs (AddService() plus the typed
/// AiClientService HttpClient), with ValidateScopes/ValidateOnBuild turned on, so tests can
/// verify the container is captive-dependency-free without spinning up a real SSH listener.
/// </summary>
internal static class GatewayServiceProviderFactory
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiServiceSettings:BaseUrl"] = "http://localhost:5247",
                ["AiServiceSettings:SharedSecret"] = "test-secret"
            })
            .Build();

        services.AddLogging();
        services.Configure<SshSettings>(configuration.GetSection("SshSettings"));
        services.Configure<AiServiceSettings>(configuration.GetSection("AiServiceSettings"));
        services.Configure<KnowledgeBaseSettings>(configuration.GetSection("KnowledgeBaseSettings"));

        services.AddService();

        services.AddHttpClient<IAiClientService, AiClientService>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<AiServiceSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
