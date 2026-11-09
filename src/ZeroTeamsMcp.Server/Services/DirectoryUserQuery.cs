namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Búsqueda del directorio. No llama a Graph: arma el $search y decide qué filas
/// ve la key restringida.
/// </summary>
public static class DirectoryUserQuery
{
    public const int DefaultTop = 50;
    public const int MaxTop = 100;

    public static int ClampTop(int top) => top < 1 ? DefaultTop : Math.Min(top, MaxTop);

    public static bool TrySearch(string? query, out string? search, out string? error)
    {
        search = null;
        error = null;
        if (string.IsNullOrWhiteSpace(query))
        {
            error = "query es obligatorio.";
            return false;
        }

        var text = query.Trim().Replace("\"", "");
        if (text.Length == 0)
        {
            error = "query es obligatorio.";
            return false;
        }

        if (text.Contains('@'))
        {
            search = "\"mail:" + text + " OR userPrincipalName:" + text + "\"";
            return true;
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        search = "\"" + string.Join(" AND ", tokens.Select(token => "displayName:" + token)) + "\"";
        return true;
    }

    public static bool HideFromRestricted(
        bool isRestricted,
        RestrictedPolicy policy,
        string? mail,
        string? upn)
    {
        if (!isRestricted) return false;
        if (!policy.EmailsConfigured) return true;
        if (string.IsNullOrWhiteSpace(mail) && string.IsNullOrWhiteSpace(upn)) return true;
        return policy.DeniesUser(mail, upn, null);
    }

    public static bool IsGraphNext(string? next)
    {
        if (string.IsNullOrWhiteSpace(next)) return true;
        return Uri.TryCreate(next, UriKind.Absolute, out var uri)
            && uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
