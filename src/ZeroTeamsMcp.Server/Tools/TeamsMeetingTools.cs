using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsMeetingTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [McpServerTool(Name = "teams_list_meetings")]
    public static async Task<string> ListMeetings(
        GraphService graph,
        [Description("Opcional. Máximo de reuniones. Default: 50.")] int top = 50)
    {
        var client = await graph.GetClientAsync();
        var result = await client.Me.OnlineMeetings.GetAsync(cfg =>
        {
            cfg.QueryParameters.Top = top;
        });

        var meetings = result?.Value?.Select(m => new
        {
            m.Id,
            m.Subject,
            m.JoinWebUrl,
            m.StartDateTime,
            m.EndDateTime,
            m.IsBroadcast,
            m.RecordAutomatically,
            m.AllowRecording,
            m.AllowTranscription,
            m.CreationDateTime
        }) ?? [];

        return JsonSerializer.Serialize(meetings, JsonOpts);
    }

    [McpServerTool(Name = "teams_get_meeting")]
    public static async Task<string> GetMeeting(
        GraphService graph,
        [Description("ID de la reunión online (UUID).")] string meetingId)
    {
        var client = await graph.GetClientAsync();
        var meeting = await client.Me.OnlineMeetings[meetingId].GetAsync();

        return JsonSerializer.Serialize(new
        {
            meeting?.Id,
            meeting?.Subject,
            meeting?.JoinWebUrl,
            meeting?.StartDateTime,
            meeting?.EndDateTime,
            meeting?.IsBroadcast,
            meeting?.RecordAutomatically,
            meeting?.AllowRecording,
            meeting?.AllowTranscription,
            meeting?.CreationDateTime,
            meeting?.ExternalId,
            meeting?.MeetingTemplateId
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_create_meeting")]
    public static async Task<string> CreateMeeting(
        GraphService graph,
        [Description("Título de la reunión.")] string subject,
        [Description("Fecha/hora inicio (ISO 8601).")] string startDateTime,
        [Description("Fecha/hora fin (ISO 8601).")] string endDateTime,
        [Description("Opcional. IDs de asistentes separados por comas.")] string? attendeeIds = null)
    {
        var client = await graph.GetClientAsync();

        var meeting = new OnlineMeeting
        {
            Subject = subject,
            StartDateTime = DateTimeOffset.Parse(startDateTime),
            EndDateTime = DateTimeOffset.Parse(endDateTime)
        };

        if (!string.IsNullOrEmpty(attendeeIds))
        {
            var ids = attendeeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            meeting.Participants = new MeetingParticipants
            {
                Attendees = ids.Select(id => new MeetingParticipantInfo
                {
                    Identity = new IdentitySet
                    {
                        User = new Identity { Id = id }
                    },
                    Role = OnlineMeetingRole.Attendee
                }).ToList()
            };
        }

        var created = await client.Me.OnlineMeetings.PostAsync(meeting);

        return JsonSerializer.Serialize(new
        {
            created?.Id,
            created?.Subject,
            created?.JoinWebUrl,
            created?.StartDateTime,
            created?.EndDateTime
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_delete_meeting")]
    public static async Task<string> DeleteMeeting(
        GraphService graph,
        [Description("ID de la reunión online (UUID).")] string meetingId)
    {
        var client = await graph.GetClientAsync();
        await client.Me.OnlineMeetings[meetingId].DeleteAsync();

        return JsonSerializer.Serialize(new
        {
            Success = true,
            Message = $"Meeting {meetingId} deleted"
        }, JsonOpts);
    }

    [McpServerTool(Name = "teams_get_meeting_attendance")]
    public static async Task<string> GetMeetingAttendance(
        GraphService graph,
        [Description("ID de la reunión online (UUID).")] string meetingId)
    {
        var client = await graph.GetClientAsync();
        var reports = await client.Me.OnlineMeetings[meetingId].AttendanceReports.GetAsync();

        var result = reports?.Value?.Select(r => new
        {
            r.Id,
            r.TotalParticipantCount,
            r.MeetingStartDateTime,
            r.MeetingEndDateTime,
            r.AttendanceRecords
        }) ?? [];

        return JsonSerializer.Serialize(result, JsonOpts);
    }
}
