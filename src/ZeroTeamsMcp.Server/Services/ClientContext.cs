using Microsoft.AspNetCore.Http;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Identidad del cliente remoto autenticado en el request HTTP actual.
/// </summary>
public sealed class ClientIdentity
{
    public string Email { get; init; } = "";
    public bool IsAdmin { get; init; }
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
    public string? Email => Identity?.Email;

    /// <summary>
    /// Resuelve el userId objetivo permitido para este request:
    /// - Admin: usa el userId pedido tal cual.
    /// - Cliente regular: fuerza su propio email (bloquea consultar otros).
    /// - Sin identidad (stdio/legacy): devuelve el userId pedido.
    /// </summary>
    public string? ResolveTarget(string? requestedUserId)
    {
        var identity = Identity;
        if (identity is null)
            return requestedUserId;

        if (identity.IsAdmin)
            return requestedUserId;

        if (!string.IsNullOrWhiteSpace(requestedUserId) &&
            !requestedUserId.Equals(identity.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Cliente limitado a su propio email. Solo el admin puede consultar otros usuarios.");
        }

        return identity.Email;
    }
}
