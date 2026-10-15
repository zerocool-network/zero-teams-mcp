using ZeroTeamsMcp.ControlPlane.Services;

namespace ZeroTeamsMcp.ControlPlane.Endpoints;

/// <summary>
/// Sesión del panel: identidad Microsoft en cookie firmada + "Mi conexión"
/// (API key y snippet de integración con Claude/OpenCode). Sin sesión no hay
/// acceso a nada de esto.
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/session");

        // Identidad actual (la usa el middleware del panel para exigir login).
        group.MapGet("/", (HttpContext http, SessionService sessions) =>
        {
            var s = sessions.Read(http);
            if (s is null) return Results.Unauthorized();
            return Results.Ok(new
            {
                s.UserId,
                s.Email,
                s.ExpiresAt,
                IsAdmin = SessionService.IsAdminEmail(s.Email)
            });
        });

        group.MapPost("/logout", (HttpContext http, SessionService sessions) =>
        {
            sessions.ClearCookie(http);
            return Results.Ok(new { ok = true });
        });

        // "Mi conexión": key del usuario para Claude/OpenCode. Si es su primer
        // login, se da de alta automáticamente (solo email validado por OAuth).
        group.MapGet("/connection", async (HttpContext http, ClientStore store) =>
        {
            var s = SessionService.ReadSession(http);
            if (s is null) return Results.Unauthorized();

            var client = await store.FindByEmailAsync(s.Email);
            var created = false;
            if (client is null)
            {
                // Alta: key de cliente (lectura). Escritura = key admin por CLI.
                await store.RegisterAsync(s.Email, isAdmin: false);
                client = await store.FindByEmailAsync(s.Email);
                created = true;
            }

            if (client is null)
                return Results.Problem("No se pudo crear la API key");

            return Results.Ok(new
            {
                Email = s.Email,
                PanelIsAdmin = SessionService.IsAdminEmail(s.Email),
                ApiKey = client.ApiKey,          // null si la key es anterior a la copia cifrada
                KeyPrefix = client.KeyPrefix,
                KeyCreated = created,
                CreatedAt = client.CreatedAt,
                ServerUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL")
                    ?? "http://localhost:5200/mcp"
            });
        });
    }
}
