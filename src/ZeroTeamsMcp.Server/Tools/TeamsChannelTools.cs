using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

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
    [Description("Lista los canales de un team. Requiere teamId (UUID). Retorna ID, nombre y tipo de membresía.")]
    public static async Task<string> ListChannels(
        GraphService graph,
        ILogger<TeamsChannelTools> logger,
        [Description("ID del team (UUID).")] string teamId,
        [Description("Opcional. Skip para paginación.")] int skip = 0,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_channels", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Teams[teamId].Channels.GetAsync();

            return result?.Value?.Select(c => new
            {
                c.Id,
                c.DisplayName,
                c.Description,
                c.WebUrl,
                MembershipType = c.MembershipType?.ToString(),
                c.CreatedDateTime
            }) ?? [];
        });
    }

    [McpServerTool(Name = "teams_create_channel")]
    [Description("Crea un canal estándar en un team. Requiere teamId y displayName.")]
    public static async Task<string> CreateChannel(
        GraphService graph,
        ILogger<TeamsChannelTools> logger,
        ClientContext clientContext,
        [Description("ID del team (UUID).")] string teamId,
        [Description("Nombre del canal.")] string displayName,
        [Description("Opcional. Descripción del canal.")] string? description = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_create_channel");
        return await logger.ExecuteWithLogging("teams_create_channel", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);

            var channel = new Channel
            {
                DisplayName = displayName,
                Description = description,
                MembershipType = ChannelMembershipType.Standard
            };

            var created = await client.Teams[teamId].Channels.PostAsync(channel);

            return new
            {
                created?.Id,
                created?.DisplayName,
                created?.Description,
                created?.WebUrl,
                MembershipType = created?.MembershipType?.ToString(),
                created?.CreatedDateTime
            };
        });
    }

    [McpServerTool(Name = "teams_send_channel_message")]
    [Description("Envía un mensaje a un canal de Teams. Requiere teamId, channelId y content.")]
    public static async Task<string> SendChannelMessage(
        GraphService graph,
        ILogger<TeamsChannelTools> logger,
        ClientContext clientContext,
        [Description("ID del team (UUID).")] string teamId,
        [Description("ID del canal (UUID).")] string channelId,
        [Description("Contenido del mensaje (texto plano o HTML).")] string content,
        [Description("Opcional. ID del mensaje al que responde.")] string? replyToId = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_send_channel_message");
        return await logger.ExecuteWithLogging("teams_send_channel_message", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);

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

            return new
            {
                sent?.Id,
                sent?.CreatedDateTime,
                Content = content
            };
        });
    }
}