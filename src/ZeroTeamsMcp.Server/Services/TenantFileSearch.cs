namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Búsqueda de archivos en todo el tenant. La región y el tope se deciden aquí,
/// antes de llamar a Graph. El tipo de archivo y el dueño se filtran después.
/// </summary>
public static class TenantFileSearch
{
    public const string Region = "EMEA";
    public const int DefaultTop = 25;
    public const int MaxTop = 50;

    public readonly record struct Request(string QueryString, int Size, string Region);

    public static bool TryRequest(string? query, int top, out Request request, out string? error)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            request = default;
            error = "query es obligatorio.";
            return false;
        }

        var size = top < 1 ? DefaultTop : Math.Min(top, MaxTop);
        request = new Request(query.Trim(), size, Region);
        error = null;
        return true;
    }

    /// <summary>
    /// fileType es la extensión, con o sin punto. Una carpeta u otro nombre
    /// sin esa extensión no pasa. Sin fileType pasa todo.
    /// </summary>
    public static bool MatchesFileType(string? name, string? fileType)
    {
        if (string.IsNullOrWhiteSpace(fileType)) return true;
        var wanted = fileType.Trim().TrimStart('.');
        if (wanted.Length == 0) return true;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return false;
        return name[(dot + 1)..].Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La key restringida tira el hit si el dueño no trae email o está excluido.
    /// Sin denylist configurada no pasa ninguno. La key dios no filtra.
    /// </summary>
    public static bool HideFromRestricted(bool isRestricted, RestrictedPolicy policy, string? ownerEmail)
    {
        if (!isRestricted) return false;
        if (!policy.EmailsConfigured) return true;
        if (string.IsNullOrWhiteSpace(ownerEmail)) return true;
        return policy.DeniesAddress(ownerEmail);
    }
}
