using System.Text.Json;
using HIM.AiService.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14C (SEC-06 / BL-2). CorrelationMiddlewareExtension pushed CorrelationId onto
/// Serilog.Context.LogContext but was never registered in Program.cs, and the AI service
/// configured no Serilog logger at all - so even once registered, the enrichment had nothing
/// reading it. Both are fixed together here: the middleware is wired in, a real Serilog logger
/// (writing to an in-memory sink instead of console/file) captures the enriched events, and
/// ErrorHandlingMiddleware turns an unhandled exception into a generic body carrying the same
/// correlation id - never the exception's own message.
/// </summary>
public class CorrelationAndErrorHandlingTests
{
    private class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static string? CorrelationIdOf(LogEvent e) =>
        e.Properties.TryGetValue("CorrelationId", out var v) && v is ScalarValue sv
            ? sv.Value?.ToString()
            : null;

    private static (WebApplication App, ListSink Sink) BuildApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var sink = new ListSink();
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        builder.Logging.AddSerilog(serilogLogger, dispose: true);

        var app = builder.Build();

        app.UseMiddleware<CorrelationMiddlewareExtension>();
        app.UseMiddleware<ErrorHandlingMiddleware>();

        app.MapGet("/ok", (ILogger<CorrelationAndErrorHandlingTests> logger) =>
        {
            logger.LogInformation("handled request");
            return Results.Ok();
        });

        app.MapGet("/throw", () =>
        {
            throw new InvalidOperationException("db=postgres://admin:hunter2@internal-host/prod");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        return (app, sink);
    }

    [Fact]
    public async Task CorrelationHeader_IsPresentOnTheEmittedLogEvent()
    {
        var (app, sink) = BuildApp();
        await using var _ = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
        request.Headers.Add("X-Request-Id", "test-correlation-123");
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(sink.Events, e => CorrelationIdOf(e) == "test-correlation-123");
    }

    [Fact]
    public async Task UnhandledException_ReturnsGenericBody_WithCorrelationId_AndNoExceptionText()
    {
        var (app, sink) = BuildApp();
        await using var _ = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        request.Headers.Add("X-Request-Id", "test-correlation-456");
        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("InvalidOperationException", body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("test-correlation-456", json.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));

        // The real detail must still reach the log, tagged with the same correlation id -
        // SEC-06 asks that the visitor gets an opaque id while the log keeps the detail.
        Assert.Contains(sink.Events, e =>
            CorrelationIdOf(e) == "test-correlation-456" &&
            e.Exception is InvalidOperationException &&
            e.Exception.Message.Contains("hunter2"));
    }
}
