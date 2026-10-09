using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ZeroTeamsMcp.ControlPlane.Services;

namespace ZeroTeamsMcp.ControlPlane.Endpoints;

public static class AuthEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Dictionary<string, (string Verifier, DateTimeOffset Expiry)> _pendingAuth = new();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth");

        group.MapGet("/login", (OAuthService oauth) =>
        {
            var verifier = oauth.GenerateCodeVerifier();
            var challenge = oauth.GenerateCodeChallenge(verifier);
            var state = Guid.NewGuid().ToString("N");
            _pendingAuth[state] = (verifier, DateTimeOffset.UtcNow.AddMinutes(10));
            var url = oauth.GetAuthorizationUrl(challenge, state);
            return Results.Ok(new { url, state });
        });

        group.MapGet("/callback", async (HttpContext http, OAuthService oauth, TokenStore store) =>
        {
            var code = http.Request.Query["code"].FirstOrDefault();
            var state = http.Request.Query["state"].FirstOrDefault();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Results.BadRequest(new { error = "Missing code or state" });

            if (!_pendingAuth.TryGetValue(state, out var pending))
                return Results.BadRequest(new { error = "Invalid or expired state" });

            _pendingAuth.Remove(state);

            if (pending.Expiry < DateTimeOffset.UtcNow)
                return Results.BadRequest(new { error = "State expired" });

            var token = await oauth.ExchangeCodeAsync(code, pending.Verifier);
            if (token is null)
                return Results.BadRequest(new { error = "Token exchange failed" });

            store.SaveToken(token);

            var html = """
                <!DOCTYPE html>
                <html>
                <head><title>ZeroTeams — Auth Success</title></head>
                <body style="font-family:system-ui;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#0a0a0a;color:#e0e0e0;">
                <div style="text-align:center;">
                    <h1 style="color:#00e5ff;">✓ Authenticated</h1>
                    <p>User: {email}</p>
                    <p style="color:#888;">You can close this window.</p>
                    <script>
                        if (window.opener) {{
                            window.opener.postMessage({{ type: 'auth-success', userId: '{userId}', email: '{email}' }}, '*');
                            window.close();
                        }}
                    </script>
                </div>
                </body>
                </html>
                """;
            html = html.Replace("{email}", token.Email).Replace("{userId}", token.UserId);
            http.Response.ContentType = "text/html";
            await http.Response.WriteAsync(html);
            return Results.Empty;
        });

        group.MapGet("/token/{userId}", async (string userId, TokenStore store) =>
        {
            var token = await store.GetTokenAsync(userId);
            if (token is null) return Results.NotFound(new { error = "No token for user" });
            return Results.Ok(new
            {
                token.AccessToken,
                token.UserId,
                token.Email,
                token.ExpiresAt,
                token.Scopes,
                HasRefreshToken = !string.IsNullOrEmpty(token.RefreshToken)
            });
        });

        group.MapPost("/refresh/{userId}", async (string userId, TokenStore store) =>
        {
            var token = await store.GetTokenAsync(userId);
            if (token is null) return Results.NotFound(new { error = "No token for user" });
            return Results.Ok(new
            {
                token.UserId,
                token.Email,
                token.ExpiresAt,
                token.Scopes
            });
        });

        group.MapDelete("/revoke/{userId}", (string userId, TokenStore store) =>
        {
            var removed = store.RevokeToken(userId);
            return removed
                ? Results.Ok(new { success = true, userId })
                : Results.NotFound(new { error = "No token for user" });
        });

        group.MapGet("/users", (TokenStore store) =>
        {
            var users = store.GetAllTokens().Select(t => new
            {
                t.UserId,
                t.Email,
                t.ExpiresAt,
                t.Scopes,
                HasRefreshToken = !string.IsNullOrEmpty(t.RefreshToken)
            });
            return Results.Ok(users);
        });

        group.MapGet("/status", (OAuthService oauth) =>
        {
            return Results.Ok(new
            {
                Configured = !string.IsNullOrEmpty(oauth.ClientId),
                oauth.ClientId,
                oauth.TenantId,
                TokenStorePath = Path.Combine(
                    Environment.GetEnvironmentVariable("DATA_DIR") ?? AppContext.BaseDirectory,
                    "data", "tokens.json")
            });
        });
    }
}
