using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Insider.Services;

namespace ZeroTeamsMcp.Insider.Tools;

/// <summary>
/// Señales de metadatos: lo que se revisa ANTES de leer contenido.
/// Son baratas, no invasivas y en la mayoría de casos ya resuelven el caso.
/// </summary>
[McpServerToolType]
public sealed class InsiderSignalTools
{
    [McpServerTool(Name = "insider_external_sharing")]
    [Description(
        "Revisa los permisos del OneDrive de un custodio: enlaces anónimos, enlaces de organización y " +
        "comparticiones con cuentas externas. Requiere caso abierto con scope 'files'.")]
    public static async Task<string> ExternalSharing(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderSignalTools> logger,
        IHttpClientFactory httpFactory,
        GraphAuthProvider authProvider,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (nominado en el caso).")] string custodian,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_external_sharing",
            caseId, custodian, query: "drive/root/permissions", async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "files");
                InsiderGuard.EnsureCustodian(c, custodian);

                var orgDomains = OrgDomains();
                var (http, token) = await AuthorizedClientAsync(httpFactory, authProvider, ct);
                var user = Uri.EscapeDataString(custodian.Trim());

                using var driveReq = new HttpRequestMessage(HttpMethod.Get,
                    $"users/{user}/drive?$select=id,name,driveType,webUrl");
                driveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var driveResp = await http.SendAsync(driveReq, ct);

                if (driveResp.StatusCode == HttpStatusCode.NotFound)
                {
                    return new InsiderResult(new
                    {
                        CaseId = c.CaseId,
                        Custodian = custodian,
                        HasDrive = false,
                        Note = "El usuario no tiene OneDrive aprovisionado. Revisa SharePoint del equipo si aplica."
                    }, 0);
                }

                driveResp.EnsureSuccessStatusCode();
                var drive = await driveResp.Content.ReadFromJsonAsync<JsonElement>(ct);

                using var permReq = new HttpRequestMessage(HttpMethod.Get,
                    $"users/{user}/drive/root/permissions" +
                    "?$select=id,roles,link,grantedToV2,grantedToIdentitiesV2,expirationDateTime,hasPassword");
                permReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var permResp = await http.SendAsync(permReq, ct);
                permResp.EnsureSuccessStatusCode();

                var permsDoc = await permResp.Content.ReadFromJsonAsync<JsonElement>(ct);
                var raw = permsDoc.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().ToList()
                    : [];

                var findings = raw.Select(p =>
                {
                    var linkScope = ReadString(p, "link", "scope");
                    var linkType = ReadString(p, "link", "type");
                    var linkUrl = ReadString(p, "link", "webUrl");
                    var grantees = ExternalGrantees(p, orgDomains);
                    var isAnonymous = string.Equals(linkScope, "anonymous", StringComparison.OrdinalIgnoreCase);
                    var isExternal = grantees.Count > 0;

                    return new
                    {
                        Id = ReadString(p, "id"),
                        Roles = ReadStringArray(p, "roles"),
                        LinkType = linkType,
                        LinkScope = linkScope,
                        WebUrl = linkUrl,
                        ExpirationDateTime = ReadString(p, "expirationDateTime"),
                        HasPassword = p.TryGetProperty("hasPassword", out var hp) && hp.ValueKind == JsonValueKind.True,
                        IsAnonymous = isAnonymous,
                        IsExternal = isExternal,
                        ExternalGrantees = grantees,
                        Risk = isAnonymous ? "alto" : isExternal ? "medio" : "bajo"
                    };
                })
                .OrderByDescending(f => f.Risk == "alto")
                .ThenByDescending(f => f.Risk == "medio")
                .ToList();

                return new InsiderResult(new
                {
                    CaseId = c.CaseId,
                    Custodian = custodian,
                    Drive = new
                    {
                        Id = ReadString(drive, "id"),
                        Name = ReadString(drive, "name"),
                        DriveType = ReadString(drive, "driveType"),
                        WebUrl = ReadString(drive, "webUrl")
                    },
                    AnonymousLinks = findings.Count(f => f.IsAnonymous),
                    ExternalGrants = findings.Count(f => f.IsExternal),
                    Permissions = findings,
                    Note = "Solo permisos de la raíz del OneDrive. Para un barrido completo de subcarpetas " +
                           "hace falta FileAccessed/SharingSet del audit log (insider_audit_search)."
                }, findings.Count);
            });
    }

    [McpServerTool(Name = "insider_audit_search")]
    [Description(
        "Consulta el audit log de Purview: operaciones por usuario (reglas creadas, reenvíos, descargas masivas, " +
        "comparticiones, inicios de sesión). Es la fuente más probatoria. Requiere AuditLog.Read.All.")]
    public static async Task<string> AuditSearch(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderSignalTools> logger,
        IHttpClientFactory httpFactory,
        GraphAuthProvider authProvider,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (nominado en el caso).")] string custodian,
        [Description("Opcional. Operaciones a filtrar (separadas por comas), ej. 'New-InboxRule,Set-Mailbox,FileDownloaded'.")] string? operations = null,
        [Description("Opcional. Desde (ISO 8601). Default: 30 días antes de 'to'.")] string? from = null,
        [Description("Opcional. Hasta (ISO 8601). Default: ahora.")] string? to = null,
        [Description("Opcional. Máximo de registros a devolver. Default 100, tope 200.")] int maxRecords = 100,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_audit_search",
            caseId, custodian, query: operations, async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "signals");
                InsiderGuard.EnsureCustodian(c, custodian);

                var (start, end) = InsiderGuard.NormalizeWindow(ParseDate(from), ParseDate(to));
                var limit = InsiderGuard.ClampTop(maxRecords);

                var ops = (operations ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                var (http, token) = await AuthorizedClientAsync(httpFactory, authProvider, ct);

                var createBody = new Dictionary<string, object?>
                {
                    ["displayName"] = $"insider-mcp {c.CaseId} {custodian} {DateTimeOffset.UtcNow:O}",
                    ["filterStartDateTime"] = start.ToUniversalTime().ToString("O"),
                    ["filterEndDateTime"] = end.ToUniversalTime().ToString("O"),
                    ["userPrincipalNameFilters"] = new[] { custodian }
                };
                if (ops.Count > 0)
                    createBody["operationFilters"] = ops;

                using var createReq = new HttpRequestMessage(HttpMethod.Post, "beta/security/auditLog/queries")
                {
                    Content = JsonContent.Create(createBody)
                };
                createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var create = await http.SendAsync(createReq, ct);

                if (!create.IsSuccessStatusCode)
                {
                    var body = await create.Content.ReadAsStringAsync(ct);
                    throw new McpException(
                        $"El audit log rechazó la consulta ({(int)create.StatusCode}). " +
                        "Requiere el permiso de aplicación 'AuditLog.Read.All' con consentimiento de admin " +
                        "y licencia que incluya Purview Audit (E3/E5). " +
                        $"Respuesta: {Truncate(body, 400)}");
                }

                var created = await create.Content.ReadFromJsonAsync<JsonElement>(ct);
                var queryId = created.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(queryId))
                    throw new McpException("El audit log no devolvió id de consulta.");

                // Purview procesa la consulta en asíncrono: se sondea hasta ~30s.
                JsonElement? records = null;
                var status = "notStarted";

                for (var attempt = 0; attempt < 15; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);

                    using var pollReq = new HttpRequestMessage(HttpMethod.Get,
                        $"beta/security/auditLog/queries/{Uri.EscapeDataString(queryId)}");
                    pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var poll = await http.SendAsync(pollReq, ct);
                    if (!poll.IsSuccessStatusCode)
                        continue;

                    var doc = await poll.Content.ReadFromJsonAsync<JsonElement>(ct);
                    status = doc.TryGetProperty("status", out var st) ? st.GetString() ?? status : status;

                    if (string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        if (doc.TryGetProperty("records", out var recList) && recList.ValueKind == JsonValueKind.Array)
                            records = recList;
                        break;
                    }

                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                        throw new McpException("Purview marcó la consulta de auditoría como fallida.");
                }

                if (records is null)
                {
                    return new InsiderResult(new
                    {
                        CaseId = c.CaseId,
                        Custodian = custodian,
                        QueryId = queryId,
                        Status = status,
                        Window = new { From = start, To = end },
                        Note = "Purview sigue procesando. Vuelve a consultar en unos minutos con el mismo rango."
                    }, 0);
                }

                var items = records.Value
                    .EnumerateArray()
                    .Take(limit)
                    .Select(r => new
                    {
                        Created = ReadString(r, "createdDateTime"),
                        Operation = ReadString(r, "operation"),
                        UserId = ReadString(r, "userId"),
                        RecordType = ReadString(r, "recordType"),
                        ClientIp = ReadString(r, "clientIP"),
                        ObjectId = ReadString(r, "objectId"),
                        Result = ReadString(r, "resultStatus")
                    })
                    .ToList();

                return new InsiderResult(new
                {
                    CaseId = c.CaseId,
                    Custodian = custodian,
                    Window = new { From = start, To = end },
                    Operations = ops,
                    TotalRecords = records.Value.GetArrayLength(),
                    Returned = items.Count,
                    ByOperation = items
                        .GroupBy(i => i.Operation ?? "unknown")
                        .Select(g => new { Operation = g.Key, Count = g.Count() })
                        .OrderByDescending(g => g.Count),
                    Records = items
                }, items.Count);
            });
    }

    // ------------------------------------------------------------- helpers

    private static async Task<(HttpClient Http, string Token)> AuthorizedClientAsync(
        IHttpClientFactory httpFactory, GraphAuthProvider authProvider, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("graph");
        var token = await authProvider.GetAccessTokenAsync(ct);
        return (http, token);
    }

    private static HashSet<string> OrgDomains() =>
        (Environment.GetEnvironmentVariable("INSIDER_ORG_DOMAINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extrae emails de grantedToV2 / grantedToIdentitiesV2 y devuelve solo los de fuera
    /// de los dominios propios. Si no hay INSIDER_ORG_DOMAINS configurado, devuelve todos
    /// y el operador decide: mejor un falso positivo visible que un externo invisible.
    /// </summary>
    private static List<string> ExternalGrantees(JsonElement permission, HashSet<string> orgDomains)
    {
        var emails = new List<string>();

        void Collect(JsonElement identitySet)
        {
            if (identitySet.ValueKind != JsonValueKind.Object) return;
            if (!identitySet.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object) return;

            // El modelo Identity de Graph no tipa 'email': viene en el JSON del identity set.
            if (user.TryGetProperty("email", out var email) &&
                email.ValueKind == JsonValueKind.String &&
                email.GetString() is { Length: > 0 } value)
            {
                emails.Add(value);
            }
        }

        if (permission.TryGetProperty("grantedToV2", out var g2))
            Collect(g2);

        if (permission.TryGetProperty("grantedToIdentitiesV2", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var identity in list.EnumerateArray())
                Collect(identity);
        }

        var distinct = emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (orgDomains.Count == 0)
            return distinct;

        return distinct
            .Where(e =>
            {
                var at = e.LastIndexOf('@');
                return at >= 0 && !orgDomains.Contains(e[(at + 1)..]);
            })
            .ToList();
    }

    private static string? ReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string[] ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToArray();
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new McpException($"Fecha inválida: '{value}'. Usa ISO 8601.");

        return parsed;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
