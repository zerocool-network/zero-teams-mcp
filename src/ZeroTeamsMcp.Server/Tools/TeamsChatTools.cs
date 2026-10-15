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
public sealed class TeamsChatTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_chats")]
    [Description("Lista los chats de un usuario. Requiere userId (email o UUID) para el admin. Clientes regulares usan automáticamente su propio email registrado. Retorna lista de chats con ID, topic y tipo.")]
    public static async Task<string> ListChats(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        ClientContext clientContext,
        [Description("Email o ID del usuario (UUID). Requerido para admin; opcional para clientes regulares (se fuerza su email).")] string? userId = null,
        [Description("Opcional. Máximo de chats a retornar. Default: 20.")] int top = 20,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var targetUserId = clientContext.ResolveTarget(userId);
        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("userId es requerido para el admin.", nameof(userId));

        return await logger.ExecuteWithLogging("teams_list_chats", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var result = await client.Users[targetUserId].Chats.GetAsync(cfg =>
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
        ClientContext clientContext,
        [Description("Tipo de chat: 'oneOnOne' o 'group'.")] string chatType,
        [Description("IDs de usuarios separados por comas (UUIDs).")] string memberIds,
        [Description("Opcional. Topic/título del chat (solo para group).")] string? topic = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_create_chat");
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
        ClientContext clientContext,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("Contenido del mensaje (texto plano o HTML).")] string content,
        [Description("Opcional. ID del mensaje al que responde.")] string? replyToId = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_send_message");
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
    [Description("Obtiene mensajes de un chat, paginando hacia atrás en el histórico si hace falta. Requiere chatId. Retorna remitente, contenido y fecha. Los clientes regulares solo pueden leer chats de los que son miembros.")]
    public static async Task<string> GetChatMessages(
        GraphService graph,
        ILogger<TeamsChatTools> logger,
        ClientContext clientContext,
        [Description("ID del chat (UUID).")] string chatId,
        [Description("Opcional. Máximo de mensajes a retornar en total. Default: 20.")] int top = 20,
        [Description("Opcional. Fecha ISO (yyyy-MM-dd) hasta la que paginar hacia atrás. Sigue pidiendo páginas antiguas hasta llegar a esta fecha o agotar maxPages.")] string? untilDate = null,
        [Description("Opcional. Máximo de páginas de Graph a recorrer al paginar hacia atrás. Default: 10.")] int maxPages = 10,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_chat_messages", authenticatedUserId, async () =>
        {
            // Cliente regular: solo puede leer chats de los que es miembro.
            await CloudAccessGuard.EnsureChatMemberAsync(
                graph, clientContext, "teams_get_chat_messages", chatId, authenticatedUserId);

            var client = await graph.GetClientAsync(authenticatedUserId);
            DateTimeOffset? until = string.IsNullOrWhiteSpace(untilDate)
                ? null
                : DateTimeOffset.Parse(untilDate).ToUniversalTime();

            // Graph limita el page size de mensajes de chat a 50.
            var pageSize = Math.Min(Math.Max(top, 1), 50);

            var collected = new List<ChatMessage>();
            var page = await client.Chats[chatId].Messages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = pageSize;
            });

            var pagesFetched = 0;
            while (page is not null)
            {
                pagesFetched++;
                if (page.Value is not null)
                {
                    collected.AddRange(page.Value);
                }

                var oldestInPage = page.Value?.Count > 0 ? page.Value[^1].CreatedDateTime : null;
                var reachedTarget = collected.Count >= top && until is null;
                var reachedDate = until is not null && oldestInPage is not null && oldestInPage <= until;
                var noMorePages = string.IsNullOrEmpty(page.OdataNextLink);

                if (reachedTarget || reachedDate || noMorePages || pagesFetched >= maxPages)
                {
                    break;
                }

                page = await client.Chats[chatId].Messages.WithUrl(page.OdataNextLink!).GetAsync();
            }

            var projected = collected.Select(m => new
            {
                m.Id,
                From = m.From?.User?.DisplayName,
                FromUserId = m.From?.User?.Id,
                Content = m.Body?.Content,
                ContentType = m.Body?.ContentType?.ToString(),
                m.CreatedDateTime,
                m.ReplyToId
            });

            if (until is not null)
            {
                projected = projected.Where(m => m.CreatedDateTime is not null && m.CreatedDateTime >= until);
                return projected.ToList();
            }

            return projected.Take(top).ToList();
        });
    }
}