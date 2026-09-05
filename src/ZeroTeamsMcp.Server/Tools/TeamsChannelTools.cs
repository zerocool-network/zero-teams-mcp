using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsChannelTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_channels")]
    public static async Task<string> ListChannels(
        GraphService graph,
        [Description("ID del team (UUID).")] string teamId,
        [Description("Opcional. Máximo de canales. Default: 50.")] int top = 50,
        [Description("Opcional. Skip para paginación.")] int skip = 0)
    {
        var client = await graph.GetClientAsync();
        var result = await client.Teams[teamId].Channels.GetAsync(cfg =>
        {
            cfg.QueryParameters.Top = top;
            cfg.QueryParameters.Skip = skip;
        });

        var channels = result?.Value?.Select(c => new
        {
            c.Id,
            c.DisplayName,
            c.Description,
            c.WebUrl,
            MembershipType = c.MembershipType?.ToString(),
            c.CreatedDateTime
        }) ?? [];

        return JsonSerializer.Serialize(channels, JsonOpts);
    }

    [McpServerTool(Name = "teams_create_channel")]
    public static async Task<string> CreateChannel(
        GraphService graph,
        [Description("ID del team (UUID).")] string teamId,
        [Description("Nombre del canal.")] string displayName,
        [Description("Opcional. Descripción del canal.")] string? description = null)
    {
        var client = await graph.GetClientAsync();

        var channel = new Channel
        {
            DisplayName = displayName,
            Description = description,
            MembershipType = ChannelMembershipType.Standard
        };

        var created = await client.Teams[teamId].Channels.PostAsync(channel);

        return JsonSerializer.Serialize(new
        {
            created?.Id,
            created?.DisplayName,
            created?.Description,
            created?.WebUrl,
            MembershipType = created?.MembershipType?.ToString(),
            created?.CreatedDateTime
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_send_channel_message")]
    public static async Task<string> SendChannelMessage(
        GraphService graph,
        [Description("ID del team (UUID).")] string teamId,
        [Description("ID del canal (UUID).")] string channelId,
        [Description("Contenido del mensaje (texto plano o HTML).")] string content,
        [Description("Opcional. ID del mensaje al que responde.")] string? replyToId = null)
    {
        var client = await graph.GetClientAsync();

        var message = new ChatMessage
        {
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = content
            },
            ReplyToId = replyToId
        };

        var sent = await client.Teams[teamId].Channels[channelId].Messages.PostAsync(message);

        return JsonSerializer.Serialize(new
        {
            sent?.Id,
            sent?.CreatedDateTime,
            Content = content
        }, JsonOpts);
    }
}
