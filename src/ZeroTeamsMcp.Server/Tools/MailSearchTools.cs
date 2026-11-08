using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Búsqueda acotada multi-buzón/rango/query con dedupe y cobertura explícita (SDD D, ADR-0010).
/// </summary>
[McpServerToolType]
public sealed class MailSearchTools
{
    [McpServerTool(Name = "mail_search_hits")]
    [Description("Búsqueda acotada de correos para investigación cronológica. Acepta varios buzones, carpetas sent/inbox, rangos yyyy-MM-dd y queries. Devuelve hits en formato triage, dedupe por messageId, coverage (done/pending/error) y truncated/nextSuggested. Límites duros: maxHits<=200, maxCalls<=30, máx 10 buzones, 12 rangos, 10 queries. No auto-pagina infinito; reporta pendientes.")]
    public static async Task<string> SearchHits(
        GraphService graph,
        ILogger<MailSearchTools> logger,
        ClientContext clientContext,
        [Description("Buzones separados por coma/; o salto de línea.")] string userIds,
        [Description("Opcional. sent,inbox (coma). Default sent,inbox.")] string? folders = null,
        [Description("Rangos: JSON [{\"since\":\"yyyy-MM-dd\",\"until\":\"yyyy-MM-dd\"}] o texto '2026-10-15..2026-10-31;2026-11-01..2026-11-08'.")] string ranges = "",
        [Description("Opcional. Queries separadas por coma/; o salto de línea.")] string? queries = null,
        [Description("Opcional. Remitente.")] string? fromAddress = null,
        [Description("Opcional. Destinatario/dominio.")] string? toAddress = null,
        [Description("Opcional. Texto en asunto.")] string? subject = null,
        [Description("Opcional. Máx hits (1..200). Default 50.")] int maxHits = 50,
        [Description("Opcional. Máx llamadas Graph/unidades (1..30). Default 12.")] int maxCalls = 12,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        maxHits = Math.Clamp(maxHits, 1, 200);
        maxCalls = Math.Clamp(maxCalls, 1, 30);

        var users = SplitList(userIds).Take(10).ToList();
        var folderList = SplitList(folders).DefaultIfEmpty("sent").Select(NormalizeFolder).Distinct().Take(4).ToList();
        if (folderList.Count == 0) folderList = ["sent", "inbox"];
        var rangeList = ParseRanges(ranges).Take(12).ToList();
        var queryList = SplitList(queries).DefaultIfEmpty("").Take(10).ToList();
        if (users.Count == 0) return JsonSerializer.Serialize(new { error = "userIds", message = "Indica al menos un buzón." }, MailDriveTools.CompactJson);

        return await logger.ExecuteWithLogging("mail_search_hits", authenticatedUserId, async () =>
        {
            var hits = new List<object>();
            var hitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var coverage = new List<object>();
            var limitsApplied = new { maxHits, maxCalls, users = users.Count, ranges = rangeList.Count, queries = queryList.Count, maxPagesPerUnit = 2 };
            var callsUsed = 0;
            var truncated = false;

            foreach (var userIdRaw in users)
            {
                var target = clientContext.ResolveTarget(userIdRaw) ?? userIdRaw;
                try
                {
                    await RestrictedAccessGuard.EnsureUserAsync(graph, clientContext, "mail_search_hits", target, authenticatedUserId);
                }
                catch (Exception ex)
                {
                    foreach (var folder in folderList)
                        foreach (var (since, until, rerr) in rangeList.DefaultIfEmpty((null, null, (string?)"ranges vacíos")))
                            foreach (var q in queryList)
                                coverage.Add(new { userId = target, folder, since, until, query = q, status = "error", message = ex.Message, callsUsed = 0 });
                    continue;
                }

                foreach (var (since, until, rerr) in rangeList)
                {
                    if (rerr is not null)
                    {
                        foreach (var folder in folderList)
                            foreach (var q in queryList)
                                coverage.Add(new { userId = target, folder, since, until, query = q, status = "error", error = "date", message = rerr, callsUsed = 0 });
                        continue;
                    }

                    foreach (var folder in folderList)
                    {
                        if (!MailListQuery.TryFolder(folder, out var folderId, out var folderError))
                        {
                            foreach (var q in queryList)
                                coverage.Add(new { userId = target, folder, since, until, query = q, status = "error", error = "folder", message = folderError, callsUsed = 0 });
                            continue;
                        }

                        var dateField = MailListQuery.DateField(folderId);
                        foreach (var q in queryList)
                        {
                            if (callsUsed >= maxCalls || hits.Count >= maxHits)
                            {
                                truncated = true;
                                coverage.Add(new { userId = target, folder, since, until, query = q, status = "pending", callsUsed = 0 });
                                continue;
                            }

                            var unitCalls = 0;
                            string? pendingCursor = null;
                            var status = "done";
                            string? statusMessage = null;
                            try
                            {
                                // D: KQL search acota por fecha; combinamos query/filtros con TrySearch
                                if (!MailListQuery.TryFilter(since, until, dateField, out var filter, out var dateError))
                                {
                                    coverage.Add(new { userId = target, folder, since, until, query = q, status = "error", error = "date", message = dateError, callsUsed = 0 });
                                    continue;
                                }
                                var hasQueryTerms = !string.IsNullOrWhiteSpace(fromAddress) || !string.IsNullOrWhiteSpace(toAddress)
                                    || !string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(subject);
                                var useSearch = hasQueryTerms;
                                string? search = null;
                                if (useSearch)
                                {
                                    if (!MailListQuery.TrySearch(since, until, fromAddress, subject, null,
                                            MailListQuery.SearchDateProperty(folderId), out search, out var searchError, toAddress, q))
                                    {
                                        coverage.Add(new { userId = target, folder, since, until, query = q, status = "error", error = "search", message = searchError, callsUsed = 0 });
                                        continue;
                                    }
                                    filter = null;
                                }

                                var client = await graph.GetClientAsync(authenticatedUserId);
                                var current = folderId is null
                                    ? await client.Users[target].Messages.GetAsync(cfg => { if (!string.IsNullOrEmpty(search)) cfg.Headers.Add("ConsistencyLevel", "eventual"); cfg.QueryParameters.Top = 25; cfg.QueryParameters.Select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "sentDateTime", "hasAttachments"]; if (!string.IsNullOrEmpty(search)) cfg.QueryParameters.Search = search; else { cfg.QueryParameters.Orderby = [$"{dateField} desc"]; if (!string.IsNullOrEmpty(filter)) cfg.QueryParameters.Filter = filter; } })
                                    : await client.Users[target].MailFolders[folderId].Messages.GetAsync(cfg => { if (!string.IsNullOrEmpty(search)) cfg.Headers.Add("ConsistencyLevel", "eventual"); cfg.QueryParameters.Top = 25; cfg.QueryParameters.Select = ["id", "subject", "from", "toRecipients", "ccRecipients", "receivedDateTime", "sentDateTime", "hasAttachments"]; if (!string.IsNullOrEmpty(search)) cfg.QueryParameters.Search = search; else { cfg.QueryParameters.Orderby = [$"{dateField} desc"]; if (!string.IsNullOrEmpty(filter)) cfg.QueryParameters.Filter = filter; } });
                                callsUsed++; unitCalls++;
                                var pages = 1;
                                while (current is not null && pages <= 2)
                                {
                                    foreach (var m in current.Value ?? [])
                                    {
                                        if (clientContext.IsRestricted && RestrictedAccessGuard.MailIsHidden(clientContext, m.From?.EmailAddress?.Address, MailDriveTools.Recipients(m.ToRecipients, m.CcRecipients)))
                                            continue;
                                        var key = target + "|" + (m.Id ?? "");
                                        if (!matchedBy.TryGetValue(key, out var mb)) matchedBy[key] = mb = new(StringComparer.OrdinalIgnoreCase);
                                        if (!string.IsNullOrWhiteSpace(q)) mb.Add(q.Trim());
                                        else mb.Add("(filtros)");
                                        if (hitKeys.Add(key) && hits.Count < maxHits)
                                        {
                                            var msgDate = dateField == "sentDateTime" ? m.SentDateTime : m.ReceivedDateTime;
                                            hits.Add(new { m.Id, Mailbox = target, Folder = folderId ?? "all", m.Subject, From = m.From?.EmailAddress?.Address, Date = msgDate, m.HasAttachments });
                                        }
                                    }
                                    if (hits.Count >= maxHits) { truncated = true; break; }
                                    if (string.IsNullOrEmpty(current.OdataNextLink) || pages >= 2) break;
                                    if (callsUsed >= maxCalls) { truncated = true; pendingCursor = current.OdataNextLink; status = "pending"; break; }
                                    var next = current.OdataNextLink;
                                    current = folderId is null
                                        ? await client.Users[target].Messages.WithUrl(next).GetAsync()
                                        : await client.Users[target].MailFolders[folderId].Messages.WithUrl(next).GetAsync();
                                    callsUsed++; unitCalls++; pages++;
                                }
                                if (current is not null && !string.IsNullOrEmpty(current.OdataNextLink) && status == "done")
                                {
                                    status = "pending";
                                    truncated = true;
                                    pendingCursor = TrimCursor(current.OdataNextLink);
                                }
                            }
                            catch (ODataError ex)
                            {
                                status = "error";
                                statusMessage = ex.Error?.Message ?? ex.Error?.Code;
                            }
                            catch (Exception ex)
                            {
                                status = "error";
                                statusMessage = ex.Message;
                            }

                            coverage.Add(new { userId = target, folder = folderId ?? "all", since, until, query = q, status, callsUsed = unitCalls, pendingCursor, message = statusMessage });
                            if (status == "pending" || truncated) truncated = true;
                        }
                        if (callsUsed >= maxCalls || hits.Count >= maxHits) truncated = true;
                    }
                }
            }

            // attach matchedBy
            var hitsOut = hits.Select(h =>
            {
                var id = (string?)h.GetType().GetProperty("Id")?.GetValue(h);
                var mbx = (string?)h.GetType().GetProperty("Mailbox")?.GetValue(h);
                var key = (mbx ?? "") + "|" + (id ?? "");
                var mb = matchedBy.TryGetValue(key, out var s) ? s.ToArray() : [];
                return new
                {
                    Id = id,
                    Mailbox = mbx,
                    Folder = h.GetType().GetProperty("Folder")?.GetValue(h),
                    Subject = h.GetType().GetProperty("Subject")?.GetValue(h),
                    From = h.GetType().GetProperty("From")?.GetValue(h),
                    Date = h.GetType().GetProperty("Date")?.GetValue(h),
                    HasAttachments = h.GetType().GetProperty("HasAttachments")?.GetValue(h),
                    MatchedBy = mb
                };
            }).ToArray();

            var nextSuggested = coverage.Where(c => (string?)c.GetType().GetProperty("status")?.GetValue(c) == "pending").Take(10)
                .Select(c => new { userId = c.GetType().GetProperty("userId")?.GetValue(c), folder = c.GetType().GetProperty("folder")?.GetValue(c), since = c.GetType().GetProperty("since")?.GetValue(c), until = c.GetType().GetProperty("until")?.GetValue(c), query = c.GetType().GetProperty("query")?.GetValue(c) }).ToArray();

            return (object)new
            {
                Format = "triage",
                Count = hitsOut.Length,
                Truncated = truncated,
                LimitsApplied = limitsApplied,
                Coverage = coverage,
                Hits = hitsOut,
                NextSuggested = nextSuggested.Length == 0 ? null : nextSuggested
            };
        }, MailDriveTools.CompactJson);
    }

    internal static List<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeFolder(string f)
    {
        var x = f.Trim().ToLowerInvariant();
        return x switch { "sentitems" or "enviados" or "enviado" => "sent", "inbox" or "entrada" or "bandeja" => "inbox", _ => x };
    }

    private static string? TrimCursor(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        // nextLink can be large; keep enough for debugging but avoid blowing tokens
        return url.Length <= 240 ? url : url[..240] + "...";
    }

    internal static List<(string? Since, string? Until, string? Error)> ParseRanges(string ranges)
    {
        if (string.IsNullOrWhiteSpace(ranges)) return [(null, null, null)];
        ranges = ranges.Trim();
        if (ranges.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(ranges);
                var list = new List<(string?, string?, string?)>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var since = el.TryGetProperty("since", out var s) ? s.GetString() : el.TryGetProperty("sinceDate", out var s2) ? s2.GetString() : null;
                    var until = el.TryGetProperty("until", out var u) ? u.GetString() : el.TryGetProperty("untilDate", out var u2) ? u2.GetString() : null;
                    list.Add((since, until, null));
                }
                return list.Count > 0 ? list : [(null, null, "ranges JSON vacío")];
            }
            catch (Exception ex) { return [(null, null, "ranges JSON inválido: " + ex.Message)]; }
        }
        var result = new List<(string?, string?, string?)>();
        foreach (var part in ranges.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = part.Contains("..", StringComparison.Ordinal) ? ".." : part.Contains("→", StringComparison.Ordinal) ? "→" : null;
            if (sep is null) { result.Add((part, part, null)); continue; }
            var bits = part.Split(sep, StringSplitOptions.TrimEntries);
            result.Add((bits.ElementAtOrDefault(0), bits.ElementAtOrDefault(1), null));
        }
        return result.Count > 0 ? result : [(null, null, null)];
    }
}
