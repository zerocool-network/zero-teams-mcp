using System.Globalization;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Carpeta, fechas y página de mail_list_messages. Sin llamadas a Graph.
/// </summary>
public static class MailListQuery
{
    public const int MaxTop = 100;
    public const int MaxPage = 30;

    public static bool TryFolder(string? folder, out string? folderId, out string? error)
    {
        folderId = null;
        error = null;
        if (string.IsNullOrWhiteSpace(folder)) return true;

        switch (folder.Trim().ToLowerInvariant())
        {
            case "all":
            case "todos":
            case "todas":
                return true;
            case "inbox":
            case "bandeja":
            case "entrada":
                folderId = "inbox";
                return true;
            case "sent":
            case "sentitems":
            case "enviados":
            case "enviado":
                folderId = "sentitems";
                return true;
            case "drafts":
            case "borradores":
                folderId = "drafts";
                return true;
            case "deleted":
            case "deleteditems":
            case "eliminados":
                folderId = "deleteditems";
                return true;
            case "junk":
            case "junkemail":
            case "spam":
                folderId = "junkemail";
                return true;
            case "archive":
            case "archivo":
                folderId = "archive";
                return true;
            default:
                error = "Carpeta no reconocida. Usa inbox, sent, drafts, deleted, junk, archive o vacío para todo el buzón.";
                return false;
        }
    }

    public static string DateField(string? folderId) =>
        folderId == "sentitems" ? "sentDateTime" : "receivedDateTime";

    public static string SearchDateProperty(string? folderId) =>
        folderId == "sentitems" ? "sent" : "received";

    public static bool TryFilter(
        string? sinceDate,
        string? untilDate,
        string dateField,
        out string? filter,
        out string? error,
        string? before = null)
    {
        filter = null;
        error = null;
        if (!TryBound(sinceDate, endOfDay: false, out var since, out error)) return false;
        if (!TryBound(untilDate, endOfDay: true, out var until, out error)) return false;
        if (!TryBound(before, endOfDay: false, out var olderThan, out error)) return false;
        if (since is not null && until is not null && since > until)
        {
            error = "sinceDate es posterior a untilDate.";
            return false;
        }

        var parts = new List<string>();
        if (since is not null)
            parts.Add($"{dateField} ge {FormatUtc(since.Value)}");
        if (until is not null)
            parts.Add($"{dateField} le {FormatUtc(until.Value)}");
        if (olderThan is not null)
            parts.Add($"{dateField} lt {FormatUtc(olderThan.Value)}");
        filter = parts.Count == 0 ? null : string.Join(" and ", parts);
        return true;
    }

    /// <summary>
    /// Búsqueda KQL. Acota por remitente, destinatario, asunto, cuerpo o adjuntos dentro de las fechas.
    /// El valor ya incluye las comillas que exige Graph en $search.
    /// </summary>
    public static bool TrySearch(
        string? sinceDate,
        string? untilDate,
        string? fromAddress,
        string? subject,
        bool? hasAttachments,
        string dateProperty,
        out string? search,
        out string? error,
        string? toAddress = null,
        string? query = null)
    {
        search = null;
        error = null;
        var terms = new List<string>();
        if (!TryParty("fromAddress", fromAddress, "from", requireAt: true, terms, out error)) return false;
        if (!TryParty("toAddress", toAddress, "recipients", requireAt: false, terms, out error)) return false;

        if (!string.IsNullOrWhiteSpace(subject))
        {
            var text = subject.Trim().Replace("\"", "");
            if (text.Length == 0)
            {
                error = "subject está vacío.";
                return false;
            }
            terms.Add(KqlTerm("subject", text));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim().Replace("\"", "");
            if (text.Length == 0)
            {
                error = "query está vacío.";
                return false;
            }
            // Una palabra es término libre (asunto y cuerpo). Varias palabras son una frase
            // exacta en esos campos. Las comillas internas van escapadas: si no, Graph responde 400.
            terms.Add(KqlTerm(null, text));
        }

        if (hasAttachments is true) terms.Add("hasAttachments:true");
        if (hasAttachments is false) terms.Add("hasAttachments:false");

        if (!string.IsNullOrWhiteSpace(sinceDate) || !string.IsNullOrWhiteSpace(untilDate))
        {
            if (!TryBound(sinceDate, endOfDay: false, out var since, out error)) return false;
            if (!TryBound(untilDate, endOfDay: true, out var until, out error)) return false;
            if (since is not null && until is not null && since > until)
            {
                error = "sinceDate es posterior a untilDate.";
                return false;
            }
            if (since is not null)
                terms.Add($"{dateProperty}>={since.Value:yyyy-MM-dd}");
            if (until is not null)
                terms.Add($"{dateProperty}<={until.Value:yyyy-MM-dd}");
        }

        if (terms.Count == 0) return true;
        search = "\"" + string.Join(" AND ", terms) + "\"";
        return true;
    }

    private static bool TryParty(
        string label,
        string? raw,
        string property,
        bool requireAt,
        List<string> terms,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var value = raw.Trim();
        if (value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\''))
        {
            error = label + " no puede llevar espacios ni comillas.";
            return false;
        }
        if (requireAt && !value.Contains('@'))
        {
            error = label + " tiene que ser un email.";
            return false;
        }
        if (!requireAt && !value.Contains('@') && !value.Contains('.'))
        {
            error = label + " tiene que ser un email o un dominio.";
            return false;
        }
        terms.Add(property + ":" + value);
        return true;
    }

    /// <summary>
    /// Graph exige que $search vaya entre comillas. Una frase lleva las suyas escapadas
    /// para no cerrar esa cadena.
    /// </summary>
    internal static string KqlTerm(string? property, string text)
    {
        var value = text.Any(char.IsWhiteSpace) ? "\\\"" + text + "\\\"" : text;
        return property is null ? value : property + ":" + value;
    }

    public static bool TryBound(string? value, bool endOfDay, out DateTimeOffset? bound, out string? error)
    {
        bound = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var text = value.Trim();
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var local = endOfDay
                ? new DateTime(day.Year, day.Month, day.Day, 23, 59, 59, DateTimeKind.Unspecified)
                : new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
            bound = new DateTimeOffset(local, tz.GetUtcOffset(local));
            return true;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            bound = parsed;
            return true;
        }

        error = $"Fecha no válida: {text}. Usa yyyy-MM-dd.";
        return false;
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
