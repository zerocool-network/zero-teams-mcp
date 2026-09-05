using System.ComponentModel;
using System.Text.Json;
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
    public static async Task<string> GetChatMembers(
        GraphService graph,
        [Description("ID del chat (UUID).")] string chatId)
    {
        var client = await graph.GetClientAsync();
        var result = await client.Chats[chatId].Members.GetAsync();

        var members = result?.Value?.Select(m => new
        {
            m.Id,
            m.DisplayName,
            Roles = m.Roles != null ? string.Join(",", m.Roles) : ""
        }) ?? [];

        return JsonSerializer.Serialize(members, JsonOpts);
    }

    [McpServerTool(Name = "teams_add_chat_member")]
    public static async Task<string> AddChatMember(
        GraphService graph,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("ID del usuario a agregar (UUID).")] string userId)
    {
        var client = await graph.GetClientAsync();

        var member = new AadUserConversationMember
        {
            Roles = new List<string>(),
            AdditionalData = new Dictionary<string, object>
            {
                ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{userId}')"
            }
        };

        var result = await client.Chats[chatId].Members.PostAsync(member as ConversationMember);

        return JsonSerializer.Serialize(new
        {
            result?.Id,
            Success = true,
            Message = $"Member {userId} added to chat {chatId}"
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_remove_chat_member")]
    public static async Task<string> RemoveChatMember(
        GraphService graph,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("ID del miembro a eliminar (UUID del conversation member).")] string memberId)
    {
        var client = await graph.GetClientAsync();
        await client.Chats[chatId].Members[memberId].DeleteAsync();

        return JsonSerializer.Serialize(new
        {
            Success = true,
            Message = $"Member {memberId} removed from chat {chatId}"
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_get_channel_members")]
    public static async Task<string> GetChannelMembers(
        GraphService graph,
        [Description("ID del team (UUID).")] string teamId,
        [Description("ID del canal (UUID).")] string channelId)
    {
        var client = await graph.GetClientAsync();
        var result = await client.Teams[teamId].Channels[channelId].Members.GetAsync();

        var members = result?.Value?.Select(m => new
        {
            m.Id,
            m.DisplayName,
            Roles = m.Roles != null ? string.Join(",", m.Roles) : ""
        }) ?? [];

        return JsonSerializer.Serialize(members, JsonOpts);
    }
}
