using Microsoft.AspNetCore.Http;

namespace ZeroTeamsMcp.Insider.Services;

/// <summary>
/// Identidad del operador que ejecuta la acción, para atribuir la auditoría.
///
/// A diferencia del MCP público, aquí no hay clientes "regulares": quien llega
/// a este servidor es el equipo de seguridad. El control de acceso es la clave
/// per-persona (<see cref="InsiderKeyring"/>), y el actor es el nombre de esa
/// clave. En stdio cae a variables de entorno para uso local.
/// </summary>
public sealed class InsiderContext
{
    private readonly IHttpContextAccessor _accessor;

    public InsiderContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string Actor
    {
        get
        {
            if (_accessor.HttpContext is not null)
            {
                if (_accessor.HttpContext.Items.TryGetValue("InsiderActor", out var actor) == true &&
                    actor is string name && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                // En modo HTTP el actor debe venir del middleware. Si no está, no se
                // inventa una identidad global: se falla cerrado.
                throw new InvalidOperationException(
                    "No se pudo resolver el actor de auditoría para esta request HTTP. " +
                    "Revisa el middleware de autenticación (X-Insider-Key).");
            }

            var fallback = Environment.GetEnvironmentVariable("INSIDER_ACTOR")
                ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                ?? Environment.GetEnvironmentVariable("ADMIN_DEFAULT_USER");

            return string.IsNullOrWhiteSpace(fallback) ? "local-operator" : fallback.Trim();
        }
    }
}
