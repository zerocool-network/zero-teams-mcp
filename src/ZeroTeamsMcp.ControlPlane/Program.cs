using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Resilience;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using System.Diagnostics;
using System.Threading.RateLimiting;
using ZeroTeamsMcp.ControlPlane.Endpoints;
using ZeroTeamsMcp.ControlPlane.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5100");

var dataDir = builder.Configuration["DATA_DIR"] ?? Environment.GetEnvironmentVariable("DATA_DIR");

var encKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
if (string.IsNullOrWhiteSpace(encKey))
{
    throw new InvalidOperationException("ENCRYPTION_KEY environment variable is required but not set");
}
if (encKey.Length < 32)
{
    throw new InvalidOperationException("ENCRYPTION_KEY must be at least 32 characters. Generate one with: openssl rand -base64 32");
}

// Redis distributed lock (optional - for multi-replica)
var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
RedisLockService? redisLock = null;
if (!string.IsNullOrWhiteSpace(redisConnection))
    {
        try
        {
            redisLock = new RedisLockService(redisConnection, "zeroteams:lock:");
            builder.Services.AddSingleton(redisLock);
            builder.Services.AddSingleton<IDistributedLockProvider>(sp => redisLock);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Redis connection failed, running without distributed lock: {ex.Message}");
        }
    }

builder.Services.AddHttpClient("oauth", client =>
{
    client.BaseAddress = new Uri("https://login.microsoftonline.com/");
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

builder.Services.AddHttpClient("graph", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/");
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

builder.Services.AddSingleton<OAuthService>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("oauth");
    return new OAuthService(http);
});

builder.Services.AddSingleton(sp => new TenantStore(dataDir, sp.GetService<RedisLockService>()));
builder.Services.AddSingleton(sp => new TokenStore(dataDir, sp.GetRequiredService<OAuthService>(), sp.GetService<RedisLockService>()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ZeroTeams MCP ControlPlane", Version = "v1" });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
));
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));

    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 50;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 5;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// OpenTelemetry
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
var serviceName = "zero-teams-controlplane";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("ZeroTeamsMcp.ControlPlane")
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

app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapTenantEndpoints();
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapMetricsEndpoints();

app.Run();