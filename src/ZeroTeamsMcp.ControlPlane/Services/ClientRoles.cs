namespace ZeroTeamsMcp.ControlPlane.Services;

/// <summary>
/// Rol de una API key cloud. <see cref="Admin"/> no tiene denylist.
/// <see cref="Restricted"/> lee el tenant salvo la denylist del proceso MCP.
/// <see cref="Client"/> solo ve su propio email.
/// </summary>
public static class ClientRoles
{
    public const string Admin = "admin";
    public const string Client = "client";
    public const string Restricted = "restricted";

    /// <summary>Email centinela de la fila restringida. No es un buzón.</summary>
    public const string RestrictedEmail = "restricted";

    public static bool IsAdmin(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase);

    public static bool IsRestricted(string? role) =>
        string.Equals(role, Restricted, StringComparison.OrdinalIgnoreCase);
}
