using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Polly;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Insider.Services;

// MCP de investigación interna — servidor separado del MCP de Teams.
//
// Aislado a propósito: binario propio, permisos de Azure propios y endpoint
// propio. Un fallo aquí no debe otorgar acceso al MCP público, y viceversa.

var transport = (Environment.GetEnvironmentVariable("TRANSPORT") ?? "http").Trim().ToLowerInvariant();

// Credenciales propias. Se prefieren las INSIDER_AZURE_* para que este servicio no
// herede accidentalmente la App Registration del MCP de Teams; AZURE_* queda como
// fallback solo para pruebas locales.
static string? Pick(string insiderName, string sharedName) =>
    Environment.GetEnvironmentVariable(insiderName) is { Length: > 0 } own
        ? own.Trim()
        : Environment.GetEnvironmentVariable(sharedName)?.Trim();

var tenantId = Pick("INSIDER_AZURE_TENANT_ID", "AZURE_TENANT_ID") ?? "";
var clientId = Pick("INSIDER_AZURE_CLIENT_ID", "AZURE_CLIENT_ID") ?? "";
var clientSecret = Pick("INSIDER_AZURE_CLIENT_SECRET", "AZURE_CLIENT_SECRET") ?? "";

var graphBaseUrl = "https://graph.microsoft.com/";

if (transport == "http")
{
    var keyring = new InsiderKeyring();
    if (!keyring.HasAny)
    {
        throw new InvalidOperationException(
            "El MCP de investigación no arranca sin credenciales de acceso. " +
            "Configura INSIDER_KEYS (formato 'nombre=clave,nombre2=clave2') o INSIDER_API_KEY. " +
            "Las claves del ControlPlane no valen aquí: son superficies distintas a propósito.");
    }

    if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        throw new InvalidOperationException(
            "Faltan INSIDER_AZURE_TENANT_ID / INSIDER_AZURE_CLIENT_ID / INSIDER_AZURE_CLIENT_SECRET. " +
            "Este servicio debe usar su propia App Registration, no la del MCP de Teams.");
    }

    var allowedIps = (Environment.GetEnvironmentVariable("INSIDER_ALLOWED_IPS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var bind = Environment.GetEnvironmentVariable("INSIDER_BIND") ?? "http://127.0.0.1:5300";

    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<InsiderContext>();
    builder.Services.AddSingleton(keyring);
    builder.Services.AddSingleton<InsiderStore>();

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
            (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests ||
             args.Outcome.Result?.StatusCode >= HttpStatusCode.InternalServerError));

        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
    });
    builder.Services.AddSingleton(sp =>
        new GraphService(sp.GetRequiredService<GraphAuthProvider>()));
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        await next();
    });

    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "insider-risk" }));

    app.Use(async (ctx, next) =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/mcp"))
        {
            await next();
            return;
        }

        if (allowedIps.Length > 0)
        {
            var remote = ctx.Connection.RemoteIpAddress;
            var permitted = remote is not null && allowedIps.Any(ip =>
                IPAddress.TryParse(ip, out var parsed) && parsed.Equals(
                    remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote));

            if (!permitted)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { error = "Origen no permitido" });
                return;
            }
        }

        var provided = ctx.Request.Headers["X-Insider-Key"].FirstOrDefault();
        var actor = keyring.Resolve(provided);

        if (actor is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid or missing X-Insider-Key" });
            return;
        }

        ctx.Items["InsiderActor"] = actor;
        await next();
    });

    app.MapMcp("/mcp");

    app.Logger.LogWarning(
        "MCP de investigación activo en {Bind} con App Registration {ClientId}. " +
        "MAIL.READ ES TENANT-WIDE: sin Application Access Policy este proceso puede leer todos los buzones " +
        "de la organización. Claves configuradas: {Actors}.",
        bind, clientId, string.Join(", ", keyring.ActorNames));

    app.Run(bind);
}
else
{
    if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        Console.Error.WriteLine("Faltan INSIDER_AZURE_TENANT_ID / INSIDER_AZURE_CLIENT_ID / INSIDER_AZURE_CLIENT_SECRET.");
        return;
    }

    // Modo local (stdio) para pruebas. Los logs van a stderr para no romper el protocolo.
    using var stdin = Console.OpenStandardInput();
    var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
        .SetMinimumLevel(LogLevel.Information);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<InsiderContext>();
    builder.Services.AddSingleton(new InsiderKeyring());
    builder.Services.AddSingleton<InsiderStore>();
    builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));

    builder.Services.AddHttpClient("graph", client =>
    {
        client.BaseAddress = new Uri(graphBaseUrl);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    });

    builder.Services.AddSingleton(sp =>
        new GraphService(sp.GetRequiredService<GraphAuthProvider>()));
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
