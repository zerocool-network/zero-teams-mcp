using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsMemberTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_get_chat_members")]
    [Description("Lista los miembros de un chat. Requiere chatId (UUID). Retorna IDs, nombres y roles.")]
    public static async Task<string> GetChatMembers(
        GraphService graph,
        ILogger<TeamsMemberTools> logger,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_chat_members", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Chats[chatId].Members.GetAsync();

            return result?.Value?.Select(m => new
            {
                m.Id,
                m.DisplayName,
                Roles = m.Roles != null ? string.Join(",", m.Roles) : ""
            }) ?? [];
        });
    }

    [McpServerTool(Name = "teams_add_chat_member")]
    [Description("Agrega un usuario a un chat existente. Requiere chatId (UUID) y userId (UUID del usuario).")]
    public static async Task<string> AddChatMember(
        GraphService graph,
        ILogger<TeamsMemberTools> logger,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("ID del usuario a agregar (UUID).")] string userId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_add_chat_member", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);

            var member = new AadUserConversationMember
            {
                Roles = new List<string>(),
                AdditionalData = new Dictionary<string, object>
                {
                    ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{userId}')"
                }
            };

            var result = await client.Chats[chatId].Members.PostAsync(member as ConversationMember);

            return new
            {
                result?.Id,
                Success = true,
                Message = $"Member {userId} added to chat {chatId}"
            };
        });
    }

    [McpServerTool(Name = "teams_remove_chat_member")]
    [Description("Elimina un miembro de un chat. Requiere chatId y memberId (ID del conversation member, no el userId).")]
    public static async Task<string> RemoveChatMember(
        GraphService graph,
        ILogger<TeamsMemberTools> logger,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("ID del miembro a eliminar (UUID del conversation member).")] string memberId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_remove_chat_member", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            await client.Chats[chatId].Members[memberId].DeleteAsync();

            return new
            {
                Success = true,
                Message = $"Member {memberId} removed from chat {chatId}"
            };
        });
    }

    [McpServerTool(Name = "teams_get_channel_members")]
    [Description("Lista los miembros de un canal de Teams. Requiere teamId y channelId (ambos UUID).")]
    public static async Task<string> GetChannelMembers(
        GraphService graph,
        ILogger<TeamsMemberTools> logger,
        [Description("ID del team (UUID).")] string teamId,
        [Description("ID del canal (UUID).")] string channelId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_channel_members", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Teams[teamId].Channels[channelId].Members.GetAsync();

            return result?.Value?.Select(m => new
            {
                m.Id,
                m.DisplayName,
                Roles = m.Roles != null ? string.Join(",", m.Roles) : ""
            }) ?? [];
        });
    }
}