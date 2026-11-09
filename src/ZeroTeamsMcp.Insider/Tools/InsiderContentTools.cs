using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Insider.Services;

namespace ZeroTeamsMcp.Insider.Tools;

/// <summary>
/// Lectura de contenido (buzón y Teams) acotada a un caso abierto y a los
/// custodios nominados en él. Todo resultado queda auditado.
/// </summary>
[McpServerToolType]
public sealed partial class InsiderContentTools
{
    [McpServerTool(Name = "insider_forwarding_rules")]
    [Description(
        "Señal de exfiltración más rentable: reglas de bandeja de entrada que reenvían o redirigen correo " +
        "a direcciones externas. No devuelve cuerpos de mensajes. Requiere caso abierto con scope 'signals'.")]
    public static async Task<string> ForwardingRules(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderContentTools> logger,
        GraphService graph,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (debe estar nominado en el caso).")] string custodian,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_forwarding_rules",
            caseId, custodian, query: "inbox/messageRules", async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "signals");
                InsiderGuard.EnsureCustodian(c, custodian);

                var client = await graph.GetClientAsync(ct: ct);
                var rules = await client.Users[custodian]
                    .MailFolders["inbox"]
                    .MessageRules
                    .GetAsync(cancellationToken: ct);

                var findings = (rules?.Value ?? [])
                    .Select(r => new
                    {
                        r.Id,
                        r.DisplayName,
                        r.Sequence,
                        r.IsEnabled,
                        ForwardsExternally = IsExternalForward(r),
                        ForwardTo = Addresses(r.Actions?.ForwardTo),
                        RedirectTo = Addresses(r.Actions?.RedirectTo),
                        ForwardAsAttachmentTo = Addresses(r.Actions?.ForwardAsAttachmentTo),
                        DeletesMessage = r.Actions?.Delete ?? false,
                        MoveToFolder = r.Actions?.MoveToFolder,
                        StopProcessingRules = r.Actions?.StopProcessingRules ?? false,
                        FromAddresses = Addresses(r.Conditions?.FromAddresses),
                        SubjectContains = r.Conditions?.SubjectContains
                    })
                    .OrderByDescending(f => f.ForwardsExternally)
                    .ThenBy(f => f.Sequence)
                    .ToList();

                return new InsiderResult(new
                {
                    Custodian = custodian,
                    CaseId = c.CaseId,
                    RuleCount = findings.Count,
                    ExternalForwardingRules = findings.Count(f => f.ForwardsExternally),
                    Rules = findings,
                    Note = findings.Any(f => f.ForwardsExternally)
                        ? "Hay reenvío a dominios externos. Revisa cuándo se creó la regla vía insider_audit_search (requiere AuditLog.Read.All)."
                        : "Sin reenvío externo en la bandeja de entrada. Revisa también otras carpetas si el caso lo justifica."
                }, findings.Count);
            });
    }

    [McpServerTool(Name = "insider_mail_search")]
    [Description(
        "Busca en el buzón de un custodio (asunto, remitente, destinatarios y fecha). El cuerpo solo se devuelve " +
        "con includeBody=true de forma explícita. Requiere caso abierto con scope 'mail'.")]
    public static async Task<string> MailSearch(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderContentTools> logger,
        GraphService graph,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (nominado en el caso).")] string custodian,
        [Description("Opcional. Consulta KQL, ej. 'from:dominio-competidor.com' o '\"propuesta\"'. Si se omite, se filtra solo por fecha.")] string? kql = null,
        [Description("Opcional. Desde (ISO 8601). Default: 30 días antes de 'to'.")] string? from = null,
        [Description("Opcional. Hasta (ISO 8601). Default: ahora.")] string? to = null,
        [Description("Opcional. Máximo de mensajes. Default 50, tope 200.")] int top = 50,
        [Description("Opcional. Devuelve el cuerpo del mensaje. Default false (solo metadatos).")] bool includeBody = false,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_mail_search",
            caseId, custodian, query: kql, async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "mail");
                InsiderGuard.EnsureCustodian(c, custodian);

                var (start, end) = InsiderGuard.NormalizeWindow(ParseDate(from), ParseDate(to));
                var limit = InsiderGuard.ClampTop(top);

                var client = await graph.GetClientAsync(ct: ct);

                var select = new List<string>
                {
                    "id", "subject", "from", "toRecipients", "ccRecipients",
                    "receivedDateTime", "sentDateTime", "hasAttachments", "webLink", "bodyPreview"
                };
                if (includeBody) select.Add("body");

                MessageCollectionResponse? page;

                if (!string.IsNullOrWhiteSpace(kql))
                {
                    var trimmedKql = kql.Trim();
                    if (trimmedKql.Contains('"') || trimmedKql.Contains('\\') ||
                        trimmedKql.Any(ch => char.IsControl(ch)))
                        throw new McpException("kql no puede contener comillas, barras invertidas ni caracteres de control.");

                    // $search y $filter no se combinan en /messages: el rango va dentro del KQL.
                    var query = $"{trimmedKql} AND received>={start:yyyy-MM-dd} AND received<={end:yyyy-MM-dd}";
                    page = await client.Users[custodian].Messages.GetAsync(cfg =>
                    {
                        cfg.QueryParameters.Search = $"\"{query}\"";
                        cfg.QueryParameters.Top = limit;
                        cfg.QueryParameters.Select = select.ToArray();
                    }, ct);
                }
                else
                {
                    page = await client.Users[custodian].Messages.GetAsync(cfg =>
                    {
                        cfg.QueryParameters.Filter =
                            $"receivedDateTime ge {start.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} and " +
                            $"receivedDateTime le {end.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
                        cfg.QueryParameters.Top = limit;
                        cfg.QueryParameters.Select = select.ToArray();
                        cfg.QueryParameters.Orderby = ["receivedDateTime desc"];
                    }, ct);
                }

                var items = (page?.Value ?? []).Select(m => new
                {
                    m.Id,
                    m.Subject,
                    From = m.From?.EmailAddress?.Address,
                    FromName = m.From?.EmailAddress?.Name,
                    To = m.ToRecipients?.Select(r => r.EmailAddress?.Address).Where(a => a is not null).ToArray(),
                    Cc = m.CcRecipients?.Select(r => r.EmailAddress?.Address).Where(a => a is not null).ToArray(),
                    m.ReceivedDateTime,
                    m.SentDateTime,
                    m.HasAttachments,
                    m.WebLink,
                    Preview = m.BodyPreview,
                    Body = includeBody ? StripHtml(m.Body?.Content) : null
                }).ToList();

                return new InsiderResult(new
                {
                    CaseId = c.CaseId,
                    Custodian = custodian,
                    Window = new { From = start, To = end },
                    Kql = kql,
                    IncludeBody = includeBody,
                    Count = items.Count,
                    Messages = items,
                    Truncated = items.Count >= limit
                }, items.Count);
            });
    }

    [McpServerTool(Name = "insider_chat_search")]
    [Description(
        "Busca en los chats de Teams de un custodio (1:1 y grupales) por palabra clave y ventana temporal. " +
        "Requiere caso abierto con scope 'chat'. Los mensajes de canales necesitan ChannelMessage.Read.All.")]
    public static async Task<string> ChatSearch(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderContentTools> logger,
        GraphService graph,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (nominado en el caso).")] string custodian,
        [Description("Palabra o frase a buscar en el cuerpo del mensaje.")] string keyword,
        [Description("Opcional. Desde (ISO 8601). Default: 30 días antes de 'to'.")] string? from = null,
        [Description("Opcional. Hasta (ISO 8601). Default: ahora.")] string? to = null,
        [Description("Opcional. Máximo de chats a inspeccionar. Default 20.")] int maxChats = 20,
        [Description("Opcional. Máximo de mensajes por chat. Default 50.")] int maxMessagesPerChat = 50,
        [Description("Opcional. Máximo de coincidencias a devolver. Default 50, tope 200.")] int maxMatches = 50,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_chat_search",
            caseId, custodian, query: keyword, async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "chat");
                InsiderGuard.EnsureCustodian(c, custodian);

                if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length < 3)
                    throw new McpException("keyword es obligatoria (mínimo 3 caracteres).");

                var (start, end) = InsiderGuard.NormalizeWindow(ParseDate(from), ParseDate(to));
                var chatLimit = Math.Clamp(maxChats, 1, 50);
                var perChat = Math.Clamp(maxMessagesPerChat, 1, 100);
                var matchLimit = InsiderGuard.ClampTop(maxMatches);

                var client = await graph.GetClientAsync(ct: ct);

                var chats = await client.Users[custodian].Chats.GetAsync(cfg =>
                {
                    cfg.QueryParameters.Top = chatLimit;
                    cfg.QueryParameters.Select = ["id", "topic", "chatType", "webUrl", "createdDateTime", "lastUpdatedDateTime"];
                }, ct);

                var matches = new List<object>();
                var chatsScanned = 0;
                var notScanned = 0;
                var needle = keyword.Trim();

                foreach (var chat in chats?.Value ?? [])
                {
                    if (matches.Count >= matchLimit) { notScanned++; continue; }
                    if (string.IsNullOrWhiteSpace(chat.Id)) continue;

                    chatsScanned++;

                    var messages = await client.Chats[chat.Id].Messages.GetAsync(cfg =>
                    {
                        cfg.QueryParameters.Top = perChat;
                    }, ct);

                    foreach (var m in messages?.Value ?? [])
                    {
                        if (matches.Count >= matchLimit) break;
                        if (m.CreatedDateTime is null) continue;
                        if (m.CreatedDateTime < start || m.CreatedDateTime > end) continue;

                        var text = StripHtml(m.Body?.Content);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        matches.Add(new
                        {
                            ChatId = chat.Id,
                            chat.Topic,
                            ChatType = chat.ChatType?.ToString(),
                            MessageId = m.Id,
                            SentBy = m.From?.User?.DisplayName ?? m.From?.Application?.DisplayName,
                            SentById = m.From?.User?.Id,
                            m.CreatedDateTime,
                            MessageType = m.MessageType?.ToString(),
                            Excerpt = Excerpt(text, needle, 240),
                            ChatWebUrl = chat.WebUrl
                        });
                    }
                }

                return new InsiderResult(new
                {
                    CaseId = c.CaseId,
                    Custodian = custodian,
                    Keyword = needle,
                    Window = new { From = start, To = end },
                    ChatsAvailable = chats?.Value?.Count ?? 0,
                    ChatsScanned = chatsScanned,
                    Matches = matches.Count,
                    Truncated = matches.Count >= matchLimit,
                    NotScannedDueToCap = notScanned,
                    Results = matches,
                    Note = "Solo chats 1:1 y grupales. Los canales de equipo requieren ChannelMessage.Read.All."
                }, matches.Count);
            });
    }

    [McpServerTool(Name = "insider_export_evidence")]
    [Description(
        "Exporta mensajes concretos (por ID) a un fichero JSON con hash SHA-256, para conservar la prueba. " +
        "Requiere caso abierto con scope 'mail'.")]
    public static async Task<string> ExportEvidence(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderContentTools> logger,
        GraphService graph,
        [Description("ID del caso abierto.")] string caseId,
        [Description("Email o UUID del custodio (nominado en el caso).")] string custodian,
        [Description("IDs de mensaje separados por comas (los devuelve insider_mail_search).")] string messageIds,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_export_evidence",
            caseId, custodian, query: messageIds, async () =>
            {
                var c = InsiderGuard.RequireOpenCase(store, caseId, "mail");
                InsiderGuard.EnsureCustodian(c, custodian);

                var ids = messageIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(InsiderGuard.MaxResults)
                    .ToList();

                if (ids.Count == 0)
                    throw new McpException("messageIds no puede estar vacío.");

                foreach (var id in ids)
                {
                    if (id.Length > 256 || id.Any(ch => char.IsWhiteSpace(ch) || ch is '/' or '\\' or '?' or '#' or '<' or '>' or '"' or '\'' or '\0'))
                        throw new McpException($"messageId inválido: '{id}'.");
                }

                var client = await graph.GetClientAsync(ct: ct);
                var exported = new List<object>();

                foreach (var id in ids)
                {
                    var m = await client.Users[custodian].Messages[id].GetAsync(cfg =>
                    {
                        cfg.QueryParameters.Select =
                        [
                            "id", "subject", "from", "toRecipients", "ccRecipients", "bccRecipients",
                            "receivedDateTime", "sentDateTime", "hasAttachments", "internetMessageId",
                            "conversationId", "webLink", "body"
                        ];
                    }, ct);

                    if (m is null) continue;

                    exported.Add(new
                    {
                        m.Id,
                        m.Subject,
                        From = m.From?.EmailAddress?.Address,
                        To = m.ToRecipients?.Select(r => r.EmailAddress?.Address).ToArray(),
                        Cc = m.CcRecipients?.Select(r => r.EmailAddress?.Address).ToArray(),
                        m.ReceivedDateTime,
                        m.SentDateTime,
                        m.InternetMessageId,
                        m.ConversationId,
                        m.WebLink,
                        BodyText = StripHtml(m.Body?.Content)
                    });
                }

                var dir = Environment.GetEnvironmentVariable("INSIDER_EXPORT_DIR") ?? "data/exports";
                Directory.CreateDirectory(dir);

                var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
                var safeCustodian = SafeFileToken(custodian);
                var path = Path.GetFullPath(Path.Combine(dir, $"{c.CaseId}_{safeCustodian}_{stamp}.json"));

                var fullDir = Path.GetFullPath(dir);
                if (!path.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new McpException("Ruta de exportación fuera del directorio permitido.");

                var payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    CaseId = c.CaseId,
                    CaseName = c.Name,
                    Custodian = custodian,
                    ExportedAt = DateTimeOffset.UtcNow,
                    ExportedBy = InsiderGuard.Actor(ctx),
                    Messages = exported
                }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                await File.WriteAllBytesAsync(path, payload, ct);

                var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

                // Deja el hash en el propio directorio: si mañana cambia el fichero, se detecta.
                await File.WriteAllTextAsync(path + ".sha256", $"{hash}  {Path.GetFileName(path)}\n", ct);

                return new InsiderResult(new
                {
                    c.CaseId,
                    Custodian = custodian,
                    ExportedMessages = exported.Count,
                    File = path,
                    Sha256 = hash,
                    SizeBytes = payload.Length,
                    Note = "Guardia el .sha256 junto al .json. Cualquier alteración posterior del fichero rompe la verificación."
                }, exported.Count);
            });
    }

    // ------------------------------------------------------------- helpers

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new McpException($"Fecha inválida: '{value}'. Usa ISO 8601 (2026-01-31 o 2026-01-31T10:00:00Z).");

        return parsed;
    }

    private static bool IsExternalForward(MessageRule rule)
    {
        var orgDomains = (Environment.GetEnvironmentVariable("INSIDER_ORG_DOMAINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recipients = Addresses(rule.Actions?.ForwardTo)
            .Concat(Addresses(rule.Actions?.RedirectTo))
            .Concat(Addresses(rule.Actions?.ForwardAsAttachmentTo))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();

        if (recipients.Count == 0)
            return false;

        // Sin dominios configurados, cualquier reenvío se marca para revisión humana.
        if (orgDomains.Count == 0)
            return true;

        return recipients.Any(a =>
        {
            var at = a.LastIndexOf('@');
            return at < 0 || !orgDomains.Contains(a[(at + 1)..]);
        });
    }

    private static string[] Addresses(IEnumerable<Recipient>? recipients) =>
        recipients?
            .Select(r => r.EmailAddress?.Address)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!)
            .ToArray() ?? [];

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var text = HtmlTagRegex().Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static string Excerpt(string text, string needle, int max)
    {
        if (text.Length <= max)
            return text;

        var idx = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return text[..max] + "…";

        var start = Math.Max(0, idx - (max / 3));
        var len = Math.Min(max, text.Length - start);
        return (start > 0 ? "…" : "") + text.Substring(start, len) + (start + len < text.Length ? "…" : "");
    }

    private static string SafeFileToken(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        return sb.ToString();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
