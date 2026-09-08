#if false // DEFERRED: requires Exchange Online Application Access Policy
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
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

    private const string ExchangePolicyErrorMessage = 
        "Meeting tools require Exchange Online Application Access Policy. " +
        "Run in Exchange Online PowerShell:\n" +
        "  Connect-ExchangeOnline -UserPrincipalName admin@yourtenant.com\n" +
        "  New-ApplicationAccessPolicy -AppId ebea4bb8-4eaa-4281-bb85-bdba74b6586a " +
        "-PolicyScopeGroupId AllUsers -AccessRight RestrictAccess\n" +
        "Wait 15-30 minutes for propagation. " +
        "See: https://learn.microsoft.com/graph/api/resources/onlinemeeting";

    private static bool IsExchangePolicyError(Exception ex)
    {
        var message = ex.Message ?? "";
        return message.Contains("ApplicationAccessPolicy", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No application access policy found", StringComparison.OrdinalIgnoreCase) ||
               (ex is ODataError odata && odata.Error?.Message?.Contains("ApplicationAccessPolicy") == true);
    }

    private static void ThrowIfExchangePolicyError(Exception ex)
    {
        if (IsExchangePolicyError(ex))
        {
            throw new InvalidOperationException(ExchangePolicyErrorMessage);
        }
    }

    [McpServerTool(Name = "teams_list_meetings")]
    [Description("Lista las reuniones online de un usuario. Requiere userId (email o UUID) y authenticatedUserId para auth delegada. Opcional: filtro de fecha inicio/fin.")]
    public static async Task<string> ListMeetings(
        GraphService graph,
        ILogger<TeamsMeetingTools> logger,
        [Description("Email o ID del usuario (UUID). Requerido.")] string userId,
        [Description("Opcional. Máximo de reuniones. Default: 20.")] int top = 20,
        [Description("Opcional. Fecha inicio (ISO 8601). Requerido por Graph API.")] string? startDateTime = null,
        [Description("Opcional. Fecha fin (ISO 8601).")] string? endDateTime = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_meetings", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            
            var start = startDateTime ?? DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var end = endDateTime ?? DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var filter = $"startDateTime ge {start} and endDateTime le {end}";

            try
            {
                var result = await client.Users[userId].OnlineMeetings.GetAsync(cfg =>
                {
                    cfg.QueryParameters.Top = top;
                    cfg.QueryParameters.Filter = filter;
                });

                return result?.Value?.Select(m => new
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
            }
            catch (Exception ex)
            {
                ThrowIfExchangePolicyError(ex);
                throw;
            }
        });
    }

    [McpServerTool(Name = "teams_get_meeting")]
    [Description("Obtiene detalles de una reunión online específica. Requiere userId y meetingId.")]
    public static async Task<string> GetMeeting(
        GraphService graph,
        ILogger<TeamsMeetingTools> logger,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("ID de la reunión online (UUID).")] string meetingId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_meeting", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var meeting = await client.Users[userId].OnlineMeetings[meetingId].GetAsync();

            return new
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
            };
        });
    }

    [McpServerTool(Name = "teams_create_meeting")]
    [Description("Crea una reunión online. Requiere userId, subject, startDateTime, endDateTime.")]
    public static async Task<string> CreateMeeting(
        GraphService graph,
        ILogger<TeamsMeetingTools> logger,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("Título de la reunión.")] string subject,
        [Description("Fecha/hora inicio (ISO 8601).")] string startDateTime,
        [Description("Fecha/hora fin (ISO 8601).")] string endDateTime,
        [Description("Opcional. IDs de asistentes separados por comas.")] string? attendeeIds = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_create_meeting", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);

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

            var created = await client.Users[userId].OnlineMeetings.PostAsync(meeting);

            return new
            {
                created?.Id,
                created?.Subject,
                created?.JoinWebUrl,
                created?.StartDateTime,
                created?.EndDateTime
            };
        });
    }

    [McpServerTool(Name = "teams_delete_meeting")]
    [Description("Elimina una reunión online. Requiere userId y meetingId.")]
    public static async Task<string> DeleteMeeting(
        GraphService graph,
        ILogger<TeamsMeetingTools> logger,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("ID de la reunión online (UUID).")] string meetingId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_delete_meeting", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            await client.Users[userId].OnlineMeetings[meetingId].DeleteAsync();

            return new
            {
                Success = true,
                Message = $"Meeting {meetingId} deleted"
            };
        });
    }

    [McpServerTool(Name = "teams_get_meeting_attendance")]
    [Description("Obtiene reportes de asistencia de una reunión. Requiere userId y meetingId.")]
    public static async Task<string> GetMeetingAttendance(
        GraphService graph,
        ILogger<TeamsMeetingTools> logger,
        [Description("Email o ID del usuario (UUID).")] string userId,
        [Description("ID de la reunión online (UUID).")] string meetingId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_get_meeting_attendance", authenticatedUserId, async () =>
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var reports = await client.Users[userId].OnlineMeetings[meetingId].AttendanceReports.GetAsync();

            return reports?.Value?.Select(r => new
            {
                r.Id,
                r.TotalParticipantCount,
                r.MeetingStartDateTime,
                r.MeetingEndDateTime,
                RecordCount = r.AttendanceRecords?.Count ?? 0
            }) ?? [];
        });
    }
}
#endif