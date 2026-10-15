using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using ZeroTeamsMcp.ControlPlane.Services;

namespace ZeroTeamsMcp.ControlPlane.Endpoints;

public static class ClientEndpoints
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private static string AdminToken =>
        Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? "";

    private static bool IsValidAdminToken(string? token)
    {
        return !string.IsNullOrEmpty(AdminToken)
            && string.Equals(token, AdminToken, StringComparison.Ordinal);
    }

    public static void MapClientEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/clients");

        // Registro de cliente remoto. Opcional adminToken para crear un cliente admin (*).
        group.MapPost("/register", async (HttpContext http, ClientStore store) =>
        {
            var body = await http.Request.ReadFromJsonAsync<RegisterRequest>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var isAdmin = !string.IsNullOrEmpty(body.AdminToken);
            if (isAdmin)
            {
                if (!IsValidAdminToken(body.AdminToken))
                    return Results.Unauthorized();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.Email) || !EmailRegex.IsMatch(body.Email.Trim()))
                    return Results.BadRequest(new { error = "email required and must be valid" });
            }

            var email = isAdmin ? "*" : body.Email!.Trim().ToLowerInvariant();
            var (apiKey, prefix) = await store.RegisterAsync(email, isAdmin);

            var mcpUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL")
                ?? "http://localhost:5200/mcp";

            return Results.Ok(new
            {
                ApiKey = apiKey,
                Email = email,
                IsAdmin = isAdmin,
                KeyPrefix = prefix,
                ServerUrl = mcpUrl
            });
        });

        // Validación de key (la usa el Server para autenticar cada request).
        group.MapGet("/validate", async (string? key, ClientStore store) =>
        {
            var client = await store.ValidateAsync(key ?? "");
            if (client is null)
                return Results.NotFound(new { error = "Invalid or revoked API key" });
            return Results.Ok(new
            {
                client.Email,
                client.IsAdmin,
                client.KeyPrefix
            });
        });

        // Listado (solo admin).
        group.MapGet("/", (HttpContext http, ClientStore store) =>
        {
            if (!IsValidAdminToken(http.Request.Headers["x-admin-token"].FirstOrDefault()))
                return Results.Unauthorized();
            var clients = store.List().Select(c => new
            {
                c.KeyPrefix,
                c.Email,
                c.IsAdmin,
                c.Revoked,
                c.CreatedAt,
                c.LastUsedAt
            });
            return Results.Ok(clients);
        });

        // Revocación (solo admin).
        group.MapDelete("/{key}", (string key, HttpContext http, ClientStore store) =>
        {
            if (!IsValidAdminToken(http.Request.Headers["x-admin-token"].FirstOrDefault()))
                return Results.Unauthorized();
            var revoked = store.Revoke(key);
            return revoked ? Results.Ok(new { revoked = true }) : Results.NotFound(new { error = "Key not found" });
        });
    }

    private sealed class RegisterRequest
    {
        public string? Email { get; init; }
        public string? AdminToken { get; init; }
    }
}
