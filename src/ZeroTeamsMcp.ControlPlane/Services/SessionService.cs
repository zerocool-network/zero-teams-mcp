using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed record SessionInfo(string UserId, string Email, DateTimeOffset ExpiresAt);

/// <summary>
/// Sesión del panel: cookie firmada con HMAC-SHA256 que identifica al usuario
/// Microsoft. El panel la exige en middleware (GET /api/session) y el
/// ControlPlane la exige para tenants, métricas y tokens delegados.
/// Sin sesión no hay acceso: el panel deja de ser público.
/// </summary>
public sealed class SessionService
{
    public const string CookieName = "zt_session";

    private readonly byte[] _key;
    private readonly TimeSpan _ttl;

    public SessionService()
    {
        // Sin SESSION_SECRET explícito se deriva de ENCRYPTION_KEY (clave
        // distinta de la de cifrado: prefijo + SHA-256).
        var secret = Environment.GetEnvironmentVariable("SESSION_SECRET");
        if (string.IsNullOrWhiteSpace(secret))
            secret = "zt-session-v1:" + (Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

        var hours = int.TryParse(Environment.GetEnvironmentVariable("SESSION_TTL_HOURS"), out var h) ? h : 24;
        _ttl = TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 30));
    }

    /// <summary>Constructor para tests (secret y TTL explícitos).</summary>
    public SessionService(string secret, TimeSpan ttl)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _ttl = ttl;
    }

    public string Create(string userId, string email)
    {
        var payload = JsonSerializer.Serialize(new
        {
            u = userId,
            e = email,
            x = DateTimeOffset.UtcNow.Add(_ttl).ToUnixTimeSeconds()
        });
        var body = Base64Url(Encoding.UTF8.GetBytes(payload));
        return $"{body}.{Base64Url(Sign(body))}";
    }

    public SessionInfo? Verify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        try
        {
            var expected = Sign(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(expected, Base64UrlDecode(parts[1]))) return null;
        }
        catch (Exception)
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
            var root = doc.RootElement;
            var exp = root.GetProperty("x").GetInt64();
            if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return new SessionInfo(
                root.GetProperty("u").GetString() ?? "",
                root.GetProperty("e").GetString() ?? "",
                DateTimeOffset.FromUnixTimeSeconds(exp));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>ADMIN_EMAILS: lista de emails (coma) con rol admin del panel.</summary>
    public static bool IsAdminEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var raw = Environment.GetEnvironmentVariable("ADMIN_EMAILS") ?? "";
        foreach (var entry in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry.Trim(), email, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // --- cookie ---

    public void WriteCookie(HttpContext ctx, string token)
    {
        ctx.Response.Cookies.Append(CookieName, token, CookieOptions(ctx, expires: DateTimeOffset.UtcNow.Add(_ttl)));
    }

    public void ClearCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(CookieName, CookieOptions(ctx, expires: DateTimeOffset.UnixEpoch));
    }

    public SessionInfo? Read(HttpContext ctx)
    {
        return Verify(ctx.Request.Cookies[CookieName]);
    }

    private static CookieOptions CookieOptions(HttpContext ctx, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = IsHttps(ctx),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires
    };

    // Detrás de nginx el ControlPlane ve HTTP: mirar X-Forwarded-Proto.
    private static bool IsHttps(HttpContext ctx) =>
        ctx.Request.IsHttps ||
        string.Equals(ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase);

    // --- guards reutilizables en los endpoints ---

    /// <summary>Sesión válida o null.</summary>
    public static SessionInfo? ReadSession(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<SessionService>().Read(ctx);

    /// <summary>true solo si hay sesión y el email está en ADMIN_EMAILS.</summary>
    public static bool RequireAdmin(HttpContext ctx)
    {
        var s = ReadSession(ctx);
        return s is not null && IsAdminEmail(s.Email);
    }

    // --- firma ---

    private byte[] Sign(string body)
    {
        return HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body));
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
        }
        return Convert.FromBase64String(b64);
    }
}
