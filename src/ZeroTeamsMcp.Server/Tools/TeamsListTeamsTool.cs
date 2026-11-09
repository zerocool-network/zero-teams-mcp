using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

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
        ClientContext clientContext,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_teams", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var teams = await client.Teams.GetAsync();
            var visible = RestrictedAccessGuard.FilterTeams(
                clientContext,
                "teams_list_teams",
                teams?.Value ?? [],
                t => t.DisplayName,
                t => t.Id);
            return visible.Select(t => new { t.Id, t.DisplayName, t.Description });
        });
    }
}