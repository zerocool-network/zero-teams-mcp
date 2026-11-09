namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Defaults de las tools de correo (ADR-0010, fase 2).
/// <c>MCP_MAIL_DEFAULTS=efficient</c> activa respuestas baratas sin romper clientes que
/// pasan parámetros explícitos. Sin la variable (o con <c>legacy</c>) se mantienen los
/// defaults de la fase 1 (<c>format=full</c>, <c>bodyFormat=html</c>, 8000 chars).
/// No afecta a auth, guards ni denylist.
/// </summary>
public static class MailDefaults
{
    internal const string EnvVar = "MCP_MAIL_DEFAULTS";

    /// <summary>True si el despliegue optó por defaults eficientes.</summary>
    public static bool Efficient => IsEfficient(Environment.GetEnvironmentVariable(EnvVar));

    internal static bool IsEfficient(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "efficient" or "compact" or "triage" or "1" or "true" => true,
            _ => false
        };
    }

    /// <summary>Máximo de caracteres por defecto del cuerpo en mail_get_message.</summary>
    public const int LegacyMaxBodyChars = 8000;
    public const int EfficientMaxBodyChars = 2000;

    public static int MaxBodyChars => Efficient ? EfficientMaxBodyChars : LegacyMaxBodyChars;
}
