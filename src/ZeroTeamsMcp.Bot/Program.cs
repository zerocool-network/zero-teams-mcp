using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.DependencyInjection;
using ZeroTeamsMcp.Bot.Handlers;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Core.Graph;

var builder = WebApplication.CreateBuilder(args);

var tenantId = builder.Configuration["AZURE_TENANT_ID"]
    ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? "";
var clientId = builder.Configuration["AZURE_CLIENT_ID"]
    ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? "";
var clientSecret = builder.Configuration["AZURE_CLIENT_SECRET"]
    ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? "";

builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));
builder.Services.AddSingleton<GraphService>();
builder.Services.AddSingleton<IBot, TeamsActivityHandler>();
builder.Services.AddSingleton<BotFrameworkAdapter>();

var app = builder.Build();

app.MapPost("/api/messages", async (
    [FromBody] Activity activity,
    [FromHeader(Name = "Authorization")] string authHeader,
    IBot bot,
    BotFrameworkAdapter adapter,
    CancellationToken ct) =>
{
    await adapter.ProcessActivityAsync(authHeader, activity, bot.OnTurnAsync, ct);
    return Results.Ok();
});

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "ZeroTeamsMcp.Bot",
    Timestamp = DateTime.UtcNow
}));

app.Run();
