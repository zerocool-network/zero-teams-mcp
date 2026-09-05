using ModelContextProtocol.Server;
using Microsoft.Graph;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsListTeamsTool
{
    [McpServerTool(Name = "teams_list_teams")]
    public static async Task<string> Execute(GraphService graph)
    {
        var client = await graph.GetClientAsync();
        var teams = await client.Teams.GetAsync();
        return System.Text.Json.JsonSerializer.Serialize(teams?.Value?.Select(t => new { t.Id, t.DisplayName, t.Description }));
    }
}
