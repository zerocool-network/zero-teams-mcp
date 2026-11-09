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
public sealed class TeamsCalendarTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_calendar_events")]
    [Description("Lista eventos del calendario de un usuario. Opcional: userId (para admin usa su default si no se pasa), startDateTime, endDateTime, top. Los clientes regulares solo pueden consultar su propio calendario.")]
    public static async Task<string> ListCalendarEvents(
        GraphService graph,
        ILogger<TeamsCalendarTools> logger,
        ClientContext clientContext,
        [Description("Opcional para admin (usa ADMIN_DEFAULT_USER). Email o ID del usuario (UUID). Los clientes regulares se fuerzan a su propio email.")] string? userId = null,
        [Description("Opcional. Fecha inicio (ISO 8601). Default: hoy.")] string? startDateTime = null,
        [Description("Opcional. Fecha fin (ISO 8601). Default: 7 días.")] string? endDateTime = null,
        [Description("Opcional. Máximo de eventos. Default: 20.")] int top = 20,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var targetUserId = clientContext.ResolveTarget(userId);
        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("userId es requerido para el admin.", nameof(userId));
        return await logger.ExecuteWithLogging("teams_list_calendar_events", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "teams_list_calendar_events", targetUserId, authenticatedUserId);

            var client = await graph.GetClientAsync(authenticatedUserId);
            var start = startDateTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var end = endDateTime ?? DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ");

            var result = await client.Users[targetUserId].CalendarView.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = top;
                cfg.QueryParameters.StartDateTime = start;
                cfg.QueryParameters.EndDateTime = end;
                cfg.QueryParameters.Orderby = ["start/dateTime"];
                if (clientContext.IsRestricted)
                {
                    cfg.QueryParameters.Select =
                    [
                        "id", "subject", "start", "end", "isAllDay", "isCancelled",
                        "hasAttachments", "onlineMeetingUrl", "organizer", "attendees"
                    ];
                }
            });

            var events = result?.Value ?? [];
            if (clientContext.IsRestricted)
            {
                events = events.Where(e => !RestrictedAccessGuard.EventIsHidden(
                    clientContext,
                    e.Organizer?.EmailAddress?.Address,
                    e.Attendees?.Select(a => a.EmailAddress?.Address))).ToList();
            }

            return events.Select(e => new
            {
                e.Id,
                e.Subject,
                StartDateTime = e.Start?.DateTime,
                StartTimeZone = e.Start?.TimeZone,
                EndDateTime = e.End?.DateTime,
                EndTimeZone = e.End?.TimeZone,
                e.IsAllDay,
                e.IsCancelled,
                e.HasAttachments,
                e.OnlineMeetingUrl
            });
        });
    }

    [McpServerTool(Name = "teams_get_calendar_event")]
    [Description("Obtiene detalles de un evento del calendario. Requiere userId y eventId.")]
    public static async Task<string> GetCalendarEvent(
        GraphService graph,
        ILogger<TeamsCalendarTools> logger,
        ClientContext clientContext,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("ID del evento (UUID).")] string eventId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var targetUserId = clientContext.ResolveTarget(userId) ?? userId;
        return await logger.ExecuteWithLogging("teams_get_calendar_event", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "teams_get_calendar_event", targetUserId, authenticatedUserId);

            var client = await graph.GetClientAsync(authenticatedUserId);
            var e = await client.Users[targetUserId].Events[eventId].GetAsync(cfg =>
            {
                if (clientContext.IsRestricted)
                {
                    cfg.QueryParameters.Select =
                    [
                        "id", "subject", "body", "start", "end", "isAllDay", "isCancelled",
                        "onlineMeetingUrl", "hasAttachments", "location", "organizer", "attendees"
                    ];
                }
            });

            if (clientContext.IsRestricted)
            {
                RestrictedAccessGuard.EnsureEventFacts(
                    clientContext,
                    "teams_get_calendar_event",
                    e?.Organizer?.EmailAddress?.Address,
                    e?.Attendees?.Select(a => a.EmailAddress?.Address));
            }

            return new
            {
                e?.Id,
                e?.Subject,
                Body = e?.Body?.Content,
                StartDateTime = e?.Start?.DateTime,
                StartTimeZone = e?.Start?.TimeZone,
                EndDateTime = e?.End?.DateTime,
                EndTimeZone = e?.End?.TimeZone,
                e?.IsAllDay,
                e?.IsCancelled,
                e?.OnlineMeetingUrl,
                e?.HasAttachments,
                e?.Location,
                AttendeeCount = e?.Attendees?.Count ?? 0
            };
        });
    }

    [McpServerTool(Name = "teams_create_calendar_event")]
    [Description("Crea un evento en el calendario. Requiere userId, subject, startDateTime, endDateTime.")]
    public static async Task<string> CreateCalendarEvent(
        GraphService graph,
        ILogger<TeamsCalendarTools> logger,
        ClientContext clientContext,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("Título del evento.")] string subject,
        [Description("Fecha/hora inicio (ISO 8601).")] string startDateTime,
        [Description("Fecha/hora fin (ISO 8601).")] string endDateTime,
        [Description("Opcional. Contenido del evento (HTML).")] string? body = null,
        [Description("Opcional. IDs de asistentes separados por comas.")] string? attendeeIds = null,
        [Description("Opcional. Crear reunión online. Default: false.")] bool createOnlineMeeting = false,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_create_calendar_event");
        return await logger.ExecuteWithLogging("teams_create_calendar_event", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);

            var evt = new Event
            {
                Subject = subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = startDateTime,
                    TimeZone = "UTC"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = endDateTime,
                    TimeZone = "UTC"
                },
                Body = body != null ? new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = body
                } : null,
                IsOnlineMeeting = createOnlineMeeting,
                OnlineMeetingProvider = createOnlineMeeting ? OnlineMeetingProviderType.TeamsForBusiness : null
            };

            if (!string.IsNullOrEmpty(attendeeIds))
            {
                var ids = attendeeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                evt.Attendees = ids.Select(id => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = id },
                    Type = AttendeeType.Required
                }).ToList();
            }

            var created = await client.Users[userId].Events.PostAsync(evt);

            return new
            {
                created?.Id,
                created?.Subject,
                StartDateTime = created?.Start?.DateTime,
                StartTimeZone = created?.Start?.TimeZone,
                EndDateTime = created?.End?.DateTime,
                EndTimeZone = created?.End?.TimeZone,
                created?.IsOnlineMeeting,
                created?.OnlineMeetingUrl,
                created?.WebLink
            };
        });
    }

    [McpServerTool(Name = "teams_delete_calendar_event")]
    [Description("Elimina un evento del calendario. Requiere userId y eventId.")]
    public static async Task<string> DeleteCalendarEvent(
        GraphService graph,
        ILogger<TeamsCalendarTools> logger,
        ClientContext clientContext,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("ID del evento (UUID).")] string eventId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        CloudAccessGuard.RequireAdmin(clientContext, "teams_delete_calendar_event");
        return await logger.ExecuteWithLogging("teams_delete_calendar_event", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            await client.Users[userId].Events[eventId].DeleteAsync();

            return new
            {
                Success = true,
                Message = $"Event {eventId} deleted"
            };
        });
    }
}