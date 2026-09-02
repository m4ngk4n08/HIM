using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14B (SEC-08): /health/live must be true throughout, /health/ready must be false until
/// indexing completes (or the hosted service reports failure), and the two must not be the same
/// check - a readiness probe that answers healthy while the index is still building is worse
/// than none. Indexing timing is controlled via a gate on a fake IKnowledgeBaseService instead
/// of the real ~90MB ONNX pipeline, since only the readiness wiring is under test here.
/// </summary>
public class HealthEndpointsTests
{
    private class GatedKnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly TaskCompletionSource _gate = new();
        private readonly bool _fail;

        public GatedKnowledgeBaseService(bool fail = false) => _fail = fail;

        public void Release() => _gate.TrySetResult();

        public async Task InitializeAsync()
        {
            await _gate.Task;
            if (_fail) throw new InvalidOperationException("simulated indexing failure");
        }

        public Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3, float minScore = float.NegativeInfinity)
            => Task.FromResult(new List<KnowledgeChunks>());
    }

    private static (WebApplication App, GatedKnowledgeBaseService Kb) BuildApp(bool fail = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var kb = new GatedKnowledgeBaseService(fail);

        builder.Services.AddSingleton<IKnowledgeBaseService>(kb);
        builder.Services.AddSingleton<KnowledgeBaseReadinessState>();
        builder.Services.AddHostedService<KnowledgeBaseIndexingHostedService>();
        builder.Services.AddHealthChecks()
            .AddCheck<KnowledgeBaseReadinessCheck>("knowledge_base", tags: new[] { "ready" });

        var app = builder.Build();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return (app, kb);
    }

    [Fact]
    public async Task Ready_IsFalse_BeforeIndexingCompletes_ThenTrue_After()
    {
        var (app, kb) = BuildApp();
        await using var _ = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        using (var beforeReady = await client.GetAsync("/health/ready"))
        using (var beforeLive = await client.GetAsync("/health/live"))
        {
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, beforeReady.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, beforeLive.StatusCode);
        }

        kb.Release();

        // Poll briefly: the hosted service's ExecuteAsync completes asynchronously after Release().
        System.Net.HttpStatusCode afterStatus = System.Net.HttpStatusCode.ServiceUnavailable;
        for (var i = 0; i < 50 && afterStatus != System.Net.HttpStatusCode.OK; i++)
        {
            using var afterReady = await client.GetAsync("/health/ready");
            afterStatus = afterReady.StatusCode;
            if (afterStatus != System.Net.HttpStatusCode.OK) await Task.Delay(20);
        }

        Assert.Equal(System.Net.HttpStatusCode.OK, afterStatus);

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Ready_StaysFalse_WhenIndexingFails()
    {
        var (app, kb) = BuildApp(fail: true);
        await using var _ = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        kb.Release();

        // Give the failing background task a chance to run and mark itself failed.
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK;
        for (var i = 0; i < 50; i++)
        {
            using var ready = await client.GetAsync("/health/ready");
            status = ready.StatusCode;
            if (status == System.Net.HttpStatusCode.ServiceUnavailable) break;
            await Task.Delay(20);
        }

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, status);

        // Live must stay unaffected by the indexing failure - the process itself is fine.
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, live.StatusCode);
    }
}
