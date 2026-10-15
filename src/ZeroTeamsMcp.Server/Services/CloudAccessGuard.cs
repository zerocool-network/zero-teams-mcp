using Microsoft.Graph.Models;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Reglas de autorización del modo cloud (multicliente).
///
/// En stdio/legacy (sin identidad cloud) no se aplican restricciones, para no
/// romper el modo local de un solo usuario.
/// </summary>
public static class CloudAccessGuard
{
    /// <summary>
    /// Los tools de escritura (mutaciones) solo están disponibles para el admin
    /// en modo cloud: los clientes regulares son de solo lectura.
    /// </summary>
    public static void RequireAdmin(ClientContext ctx, string tool)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (ctx.IsCloud && !ctx.IsAdmin)
        {
            throw new UnauthorizedAccessException(
                $"El tool '{tool}' requiere permisos de administrador. " +
                "Los clientes regulares tienen acceso de solo lectura.");
        }
    }

    /// <summary>
    /// Comprueba que un cliente regular sea miembro del chat indicado.
    /// El admin y el modo stdio no se ven afectados.
    /// </summary>
    public static void EnsureChatMember(
        string tool,
        string chatId,
        bool isCloud,
        bool isAdmin,
        string? clientEmail,
        IEnumerable<string?> memberEmails)
    {
        ArgumentNullException.ThrowIfNull(memberEmails);

        if (!isCloud || isAdmin)
            return;

        if (string.IsNullOrWhiteSpace(clientEmail))
        {
            throw new UnauthorizedAccessException(
                $"El tool '{tool}' requiere una identidad de cliente válida.");
        }

        var isMember = memberEmails.Any(e =>
            !string.IsNullOrWhiteSpace(e) &&
            string.Equals(e, clientEmail, StringComparison.OrdinalIgnoreCase));

        if (!isMember)
        {
            throw new UnauthorizedAccessException(
                $"Acceso denegado: el cliente no es miembro del chat '{chatId}'.");
        }
    }

    /// <summary>
    /// Variante con Graph: consulta los miembros del chat y valida la pertenencia
    /// antes de permitir la operación a un cliente regular.
    /// </summary>
    public static async Task EnsureChatMemberAsync(
        GraphService graph,
        ClientContext ctx,
        string tool,
        string chatId,
        string? authenticatedUserId)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!ctx.IsCloud || ctx.IsAdmin)
            return;

        ArgumentNullException.ThrowIfNull(graph);

        var client = await graph.GetClientAsync(authenticatedUserId);
        var members = await client.Chats[chatId].Members.GetAsync();

        var emails = members?.Value?
            .Select(m => (m as AadUserConversationMember)?.Email) ?? [];

        EnsureChatMember(tool, chatId, ctx.IsCloud, ctx.IsAdmin, ctx.Email, emails);
    }
}
