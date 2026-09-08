using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsChatTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_chats")]
    [Description("Lista los chats de un usuario. Requiere userId (email o UUID). Retorna lista de chats con ID, topic y tipo.")]
    public static async Task<string> ListChats(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        [Description("Email o ID del usuario (UUID). Requerido.")] string userId,
        [Description("Opcional. Máximo de chats a retornar. Default: 20.")] int top = 20,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_chats", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Users[userId].Chats.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = top;
            });

            return result?.Value?.Select(c => new
            {
                c.Id,
                Topic = c.Topic,
                ChatType = c.ChatType?.ToString(),
                c.WebUrl,
                c.CreatedDateTime
            }) ?? [];
        });
    }

    [McpServerTool(Name = "teams_create_chat")]
    [Description("Crea un chat en Teams. chatType: 'oneOnOne' o 'group'. memberIds: UUIDs separados por comas.")]
    public static async Task<string> CreateChat(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        [Description("Tipo de chat: 'oneOnOne' o 'group'.")] string chatType,
        [Description("IDs de usuarios separados por comas (UUIDs).")] string memberIds,
        [Description("Opcional. Topic/título del chat (solo para group).")] string? topic = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_create_chat", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var ids = memberIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var members = ids.Select(id => new AadUserConversationMember
            {
                Roles = new List<string>(),
                AdditionalData = new Dictionary<string, object>
                {
                    ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{id}')"
                }
            }).ToList<ConversationMember>();

            var chat = new Chat
            {
                ChatType = chatType == "group" ? ChatType.Group : ChatType.OneOnOne,
                Members = members
            };

            if (chatType == "group" && !string.IsNullOrEmpty(topic))
            {
                chat.Topic = topic;
            }

            var created = await client.Chats.PostAsync(chat);

            return new
            {
                created?.Id,
                created?.Topic,
                ChatType = created?.ChatType?.ToString(),
                created?.WebUrl,
                created?.CreatedDateTime
            };
        });
    }

    [McpServerTool(Name = "teams_send_message")]
    [Description("Envía un mensaje de texto a un chat de Teams. Requiere chatId y content.")]
    public static async Task<string> SendMessage(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("Contenido del mensaje (texto plano o HTML).")] string content,
        [Description("Opcional. ID del mensaje al que responde.")] string? replyToId = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_send_message", authenticatedUserId, async () =>
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

            var sent = await client.Chats[chatId].Messages.PostAsync(message);

            return new
            {
                sent?.Id,
                sent?.CreatedDateTime,
                Content = content
            };
        });
    }

    [McpServerTool(Name = "teams_get_chat_messages")]
    [Description("Obtiene los mensajes recientes de un chat. Requiere chatId. Retorna remitente, contenido y fecha.")]
    public static async Task<string> GetChatMessages(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("Opcional. Máximo de mensajes. Default: 20.")] int top = 20,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_chat_messages", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Chats[chatId].Messages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = top;
            });

            return result?.Value?.Select(m => new
            {
                m.Id,
                From = m.From?.User?.DisplayName,
                FromUserId = m.From?.User?.Id,
                Content = m.Body?.Content,
                ContentType = m.Body?.ContentType?.ToString(),
                m.CreatedDateTime,
                m.ReplyToId
            }) ?? [];
        });
    }
}