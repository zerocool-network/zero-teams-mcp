using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// La key que no es admin solo anuncia tools de lectura. Si el agente llama
/// igual a un tool de escritura, la llamada se rechaza antes de Graph.
/// </summary>
public static class ReadOnlyToolFilter
{
    public static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        "teams_send_message",
        "teams_create_chat",
        "teams_send_channel_message",
        "teams_create_channel",
        "teams_create_calendar_event",
        "teams_delete_calendar_event",
        "teams_add_chat_member",
        "teams_remove_chat_member",
        "teams_create_meeting",
        "teams_delete_meeting",
        "admin_list_tenants",
        "admin_get_tenant",
        "admin_create_tenant",
        "admin_delete_tenant",
        "admin_get_consent",
        "admin_health",
    };

    public static IMcpServerBuilder WithReadOnlyCloudTools(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(FilterList);
            filters.AddCallToolFilter(FilterCall);
        });

    public static bool HidesTool(string? toolName, ClientIdentity? identity) =>
        identity is not null &&
        !identity.IsAdmin &&
        toolName is not null &&
        (WriteTools.Contains(toolName));

    private static McpRequestHandler<ListToolsRequestParams, ListToolsResult> FilterList(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            var identity = Identity(request.Services);
            if (identity is null || identity.IsAdmin)
                return result;

            result.Tools = result.Tools.Where(t => !HidesTool(t.Name, identity)).ToList();
            return result;
        };

    private static McpRequestHandler<CallToolRequestParams, CallToolResult> FilterCall(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            if (HidesTool(request.Params?.Name, Identity(request.Services)))
            {
                throw new McpException(
                    "Este acceso es de solo lectura. No se pueden enviar mensajes ni crear chats, canales o eventos.");
            }

            return await next(request, cancellationToken);
        };

    private static ClientIdentity? Identity(IServiceProvider? services)
    {
        if (services is null) return null;
        var http = services.GetService<IHttpContextAccessor>()?.HttpContext;
        return http?.Items["CloudClient"] as ClientIdentity;
    }
}
