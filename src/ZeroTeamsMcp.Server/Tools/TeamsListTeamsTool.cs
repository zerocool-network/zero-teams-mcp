using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsListTeamsTool
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_teams")]
    [Description("Lista todos los teams de Microsoft Teams del tenant. Retorna IDs y nombres.")]
    public static async Task<string> Execute(
        GraphService graph,
        ILogger<TeamsListTeamsTool> logger,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_teams", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var teams = await client.Teams.GetAsync();
            return teams?.Value?.Select(t => new { t.Id, t.DisplayName, t.Description }) ?? [];
        });
    }
}