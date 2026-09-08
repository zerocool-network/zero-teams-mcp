using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Resilience;
using ModelContextProtocol.Server;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Core.Graph;

var transport = Environment.GetEnvironmentVariable("TRANSPORT") ?? "stdio";
var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? "";
var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? "";
var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? "";
var controlPlaneUrl = Environment.GetEnvironmentVariable("CONTROLPLANE_URL") ?? "http://localhost:5100";

var encKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
if (string.IsNullOrWhiteSpace(encKey))
{
    throw new InvalidOperationException("ENCRYPTION_KEY environment variable is required but not set");
}
if (encKey.Length < 32)
{
    throw new InvalidOperationException("ENCRYPTION_KEY must be at least 32 characters. Generate one with: openssl rand -base64 32");
}

var graphBaseUrl = "https://graph.microsoft.com/";

if (transport.Equals("http", StringComparison.OrdinalIgnoreCase))
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddCors(o => o.AddPolicy("mcp", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);
    builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));
    
    builder.Services.AddHttpClient("graph", client =>
    {
        client.BaseAddress = new Uri(graphBaseUrl);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is not null ||
            (args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
             args.Outcome.Result?.StatusCode >= System.Net.HttpStatusCode.InternalServerError));
        
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddSingleton(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("graph");
        return new DelegatedAuthProvider(http, controlPlaneUrl);
    });
    builder.Services.AddSingleton(sp =>
    {
        var auth = sp.GetRequiredService<GraphAuthProvider>();
        var delegated = sp.GetRequiredService<DelegatedAuthProvider>();
        return new GraphService(auth, delegated);
    });
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    // OpenTelemetry (HTTP transport only)
    var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    var serviceName = "zero-teams-mcp-server";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("ZeroTeamsMcp.Server")
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

    var app = builder.Build();
    
    // Security headers
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';";
        await next();
    });
    
    app.UseCors("mcp");
    app.MapMcp("/mcp");
    app.Run("http://0.0.0.0:5200");
}
else
{
    using var stdin = Console.OpenStandardInput();
    var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);
    builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));
    
    builder.Services.AddHttpClient("graph", client =>
    {
        client.BaseAddress = new Uri(graphBaseUrl);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is not null ||
            (args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
             args.Outcome.Result?.StatusCode >= System.Net.HttpStatusCode.InternalServerError));
        
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddSingleton(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("graph");
        return new DelegatedAuthProvider(http, controlPlaneUrl);
    });
    builder.Services.AddSingleton(sp =>
    {
        var auth = sp.GetRequiredService<GraphAuthProvider>();
        var delegated = sp.GetRequiredService<DelegatedAuthProvider>();
        return new GraphService(auth, delegated);
    });
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}