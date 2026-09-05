using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Core.Graph;

var transport = Environment.GetEnvironmentVariable("TRANSPORT") ?? "stdio";
var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? "";
var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? "";
var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? "";

if (transport.Equals("http", StringComparison.OrdinalIgnoreCase))
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));
    builder.Services.AddSingleton<GraphService>();
    builder.Services.AddHttpClient();
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp("/mcp");
    app.Run("http://0.0.0.0:5200");
}
else
{
    using var stdin = Console.OpenStandardInput();
    var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
    builder.Services.AddSingleton(new GraphAuthProvider(tenantId, clientId, clientSecret));
    builder.Services.AddSingleton<GraphService>();
    builder.Services.AddHttpClient();
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
