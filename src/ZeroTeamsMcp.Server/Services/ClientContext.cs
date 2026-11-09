using Microsoft.AspNetCore.Http;
using ModelContextProtocol;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Identidad del cliente remoto autenticado en el request HTTP actual.
/// </summary>
public sealed class ClientIdentity
{
    public string Email { get; init; } = "";
    public bool IsAdmin { get; init; }
    public string Role { get; init; } = "";

    public bool IsRestricted =>
        string.Equals(Role, "restricted", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Scope del cliente autenticado (cloud). Se lee de HttpContext.Items,
/// poblado por el middleware de autenticación de API key. En stdio no hay
/// identidad => comportamiento legacy sin scoping.
/// </summary>
public sealed class ClientContext
{
    private readonly IHttpContextAccessor _accessor;

    public ClientContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClientIdentity? Identity =>
        _accessor.HttpContext?.Items.TryGetValue("CloudClient", out var v) == true
            ? v as ClientIdentity
            : null;

    public bool IsCloud => Identity is not null;
    public bool IsAdmin => Identity?.IsAdmin == true;
    public bool IsRestricted => Identity?.IsRestricted == true;
    public string? Email => Identity?.Email;

    /// <summary>
    /// Resuelve el userId objetivo permitido para este request:
    /// - Admin: usa el userId pedido tal cual, o un default si no se especifica (compatibilidad con clientes antiguos que no mandan userId).
    /// - Cliente regular: fuerza su propio email (bloquea consultar otros).
    /// - Sin identidad (stdio/legacy): devuelve el userId pedido.
    /// </summary>
    public string? ResolveTarget(string? requestedUserId)
    {
        var identity = Identity;
        if (identity is null)
        {
            if (!string.IsNullOrWhiteSpace(requestedUserId))
                return requestedUserId;

            var fallback = Environment.GetEnvironmentVariable("ADMIN_DEFAULT_USER")
                ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                ?? Environment.GetEnvironmentVariable("DEFAULT_USER_ID");
            return !string.IsNullOrWhiteSpace(fallback) ? fallback.Trim() : requestedUserId;
        }

        if (identity.IsAdmin || identity.IsRestricted)
        {
            if (!string.IsNullOrWhiteSpace(requestedUserId))
                return requestedUserId;

            // Compatibilidad: cliente no envía userId. El centinela restricted no es un buzón.
            if (!identity.IsRestricted &&
                !string.IsNullOrWhiteSpace(identity.Email) &&
                identity.Email != "*")
                return identity.Email;

            var adminDefault = Environment.GetEnvironmentVariable("ADMIN_DEFAULT_USER") 
                ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                ?? Environment.GetEnvironmentVariable("DEFAULT_USER_ID");
            if (!string.IsNullOrWhiteSpace(adminDefault))
                return adminDefault.Trim();

            return requestedUserId; // los tools validarán y darán error claro
        }

        if (!string.IsNullOrWhiteSpace(requestedUserId) &&
            !requestedUserId.Equals(identity.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                "Cliente limitado a su propio email. Solo el admin puede consultar otros usuarios.");
        }

        return identity.Email;
    }
}
