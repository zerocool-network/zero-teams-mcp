using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

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
    public static async Task<string> ListCalendarEvents(
        GraphService graph,
        [Description("Opcional. Fecha inicio (ISO 8601). Default: hoy.")] string? startDateTime = null,
        [Description("Opcional. Fecha fin (ISO 8601). Default: 7 días.")] string? endDateTime = null,
        [Description("Opcional. Máximo de eventos. Default: 50.")] int top = 50)
    {
        var client = await graph.GetClientAsync();
        var start = startDateTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var end = endDateTime ?? DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var result = await client.Me.CalendarView.GetAsync(cfg =>
        {
            cfg.QueryParameters.Top = top;
            cfg.QueryParameters.StartDateTime = start;
            cfg.QueryParameters.EndDateTime = end;
            cfg.QueryParameters.Orderby = ["start/dateTime"];
        });

        var events = result?.Value?.Select(e => new
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
        }) ?? [];

        return JsonSerializer.Serialize(events, JsonOpts);
    }

    [McpServerTool(Name = "teams_get_calendar_event")]
    public static async Task<string> GetCalendarEvent(
        GraphService graph,
        [Description("ID del evento (UUID).")] string eventId)
    {
        var client = await graph.GetClientAsync();
        var e = await client.Me.Events[eventId].GetAsync();

        return JsonSerializer.Serialize(new
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
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_create_calendar_event")]
    public static async Task<string> CreateCalendarEvent(
        GraphService graph,
        [Description("Título del evento.")] string subject,
        [Description("Fecha/hora inicio (ISO 8601).")] string startDateTime,
        [Description("Fecha/hora fin (ISO 8601).")] string endDateTime,
        [Description("Opcional. Contenido del evento (HTML).")] string? body = null,
        [Description("Opcional. IDs de asistentes separados por comas.")] string? attendeeIds = null,
        [Description("Opcional. Crear reunión online. Default: false.")] bool createOnlineMeeting = false)
    {
        var client = await graph.GetClientAsync();

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

        var created = await client.Me.Events.PostAsync(evt);

        return JsonSerializer.Serialize(new
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
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_delete_calendar_event")]
    public static async Task<string> DeleteCalendarEvent(
        GraphService graph,
        [Description("ID del evento (UUID).")] string eventId)
    {
        var client = await graph.GetClientAsync();
        await client.Me.Events[eventId].DeleteAsync();

        return JsonSerializer.Serialize(new
        {
            Success = true,
            Message = $"Event {eventId} deleted"
        }, JsonOpts);
    }
}
