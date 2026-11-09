namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Denylist de la key restringida. Se lee del entorno del proceso MCP en cada
/// comprobación, así no queda atrapada en el caché de 5 minutos de la API key.
/// </summary>
public sealed class RestrictedPolicy
{
    private readonly HashSet<string> _emails;
    private readonly HashSet<string> _teamNames;
    private readonly HashSet<string> _teamIds;

    private RestrictedPolicy(HashSet<string> emails, HashSet<string> teamNames, HashSet<string> teamIds)
    {
        _emails = emails;
        _teamNames = teamNames;
        _teamIds = teamIds;
    }

    public bool EmailsConfigured => _emails.Count > 0;

    public static RestrictedPolicy FromEnvironment() => Parse(
        Environment.GetEnvironmentVariable("RESTRICTED_DENY_EMAILS"),
        Environment.GetEnvironmentVariable("RESTRICTED_DENY_TEAMS"),
        Environment.GetEnvironmentVariable("RESTRICTED_DENY_TEAM_IDS"));

    public static RestrictedPolicy Parse(string? emails, string? teamNames, string? teamIds)
    {
        return new RestrictedPolicy(
            SplitEmails(emails),
            SplitTeamNames(teamNames),
            SplitTeamIds(teamIds));
    }

    public bool DeniesAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        return _emails.Contains(address.Trim().ToLowerInvariant());
    }

    public bool DeniesUser(string? mail, string? upn, IEnumerable<string?>? proxyAddresses)
    {
        if (DeniesAddress(mail) || DeniesAddress(upn)) return true;
        if (proxyAddresses is null) return false;
        foreach (var proxy in proxyAddresses)
        {
            if (string.IsNullOrWhiteSpace(proxy)) continue;
            var address = proxy;
            var colon = proxy.IndexOf(':');
            if (colon >= 0 && colon < proxy.Length - 1)
                address = proxy[(colon + 1)..];
            if (DeniesAddress(address)) return true;
        }
        return false;
    }

    public bool UserHasNoAddress(string? mail, string? upn, IEnumerable<string?>? proxyAddresses)
    {
        if (!string.IsNullOrWhiteSpace(mail) || !string.IsNullOrWhiteSpace(upn))
            return false;
        return proxyAddresses is null || !proxyAddresses.Any(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>
    /// Nombre exacto (trim, ordinal) o GUID configurado. Nombre nulo no coincide:
    /// el llamador decide si eso es fail-closed.
    /// </summary>
    public bool DeniesTeam(string? displayName, string? teamId)
    {
        if (!string.IsNullOrWhiteSpace(teamId) &&
            _teamIds.Contains(teamId.Trim().ToLowerInvariant()))
            return true;

        if (string.IsNullOrWhiteSpace(displayName)) return false;
        return _teamNames.Contains(displayName.Trim());
    }

    private static HashSet<string> SplitEmails(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('@'))
                set.Add(part.ToLowerInvariant());
        }
        return set;
    }

    private static HashSet<string> SplitTeamNames(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part);
        return set;
    }

    private static HashSet<string> SplitTeamIds(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part.ToLowerInvariant());
        return set;
    }
}
