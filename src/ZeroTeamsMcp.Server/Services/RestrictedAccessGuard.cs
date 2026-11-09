using Microsoft.Graph;
using Microsoft.Graph.Models;
using ModelContextProtocol;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Denylist de la key <c>restricted</c>. La key dios (<c>IsAdmin</c>), el cliente
/// regular y stdio no entran aquí. Si no se puede demostrar que el recurso está
/// fuera de la lista, se deniega.
/// </summary>
public static class RestrictedAccessGuard
{
    public static void EnsureConfigured(ClientContext ctx, string tool, RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return;
        policy ??= RestrictedPolicy.FromEnvironment();
        if (!policy.EmailsConfigured)
        {
            throw new McpException(
                $"Acceso denegado en '{tool}': la denylist de la key restringida no está configurada.");
        }
    }

    public static void EnsureUserFacts(
        ClientContext ctx,
        string tool,
        string? mail,
        string? upn,
        IEnumerable<string?>? proxyAddresses,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return;
        policy ??= RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        if (policy.UserHasNoAddress(mail, upn, proxyAddresses) || policy.DeniesUser(mail, upn, proxyAddresses))
        {
            throw new McpException(
                $"Acceso denegado en '{tool}': la cuenta está excluida o no se pudo verificar.");
        }
    }

    public static void EnsureTeamFacts(
        ClientContext ctx,
        string tool,
        string? displayName,
        string? teamId,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return;
        policy ??= RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        if (string.IsNullOrWhiteSpace(displayName) || policy.DeniesTeam(displayName, teamId))
        {
            throw new McpException(
                $"Acceso denegado en '{tool}': el equipo está excluido o no se pudo verificar.");
        }
    }

    /// <summary>
    /// Hechos ya resueltos de un chat. <paramref name="unresolvedMember"/> es un
    /// miembro sin email y sin identidad Graph usable.
    /// </summary>
    public static void EnsureChatFacts(
        ClientContext ctx,
        string tool,
        bool unresolvedMember,
        IEnumerable<string?> memberAddresses,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return;
        policy ??= RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        if (unresolvedMember || memberAddresses.Any(policy.DeniesAddress))
        {
            throw new McpException(
                $"Acceso denegado en '{tool}': el chat incluye una cuenta excluida o no se pudo verificar.");
        }
    }

    public static bool EventIsHidden(
        ClientContext ctx,
        string? organizer,
        IEnumerable<string?>? attendees,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return false;
        policy ??= RestrictedPolicy.FromEnvironment();
        if (!policy.EmailsConfigured) return true;
        if (string.IsNullOrWhiteSpace(organizer) || attendees is null)
            return true;
        if (attendees.Any(string.IsNullOrWhiteSpace))
            return true;
        if (policy.DeniesAddress(organizer))
            return true;
        return attendees.Any(policy.DeniesAddress);
    }

    /// <summary>
    /// Mensaje de un buzón permitido que toca una cuenta excluida. Se oculta entero.
    /// </summary>
    public static bool MailIsHidden(
        ClientContext ctx,
        string? from,
        IEnumerable<string?>? recipients,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return false;
        policy ??= RestrictedPolicy.FromEnvironment();
        if (!policy.EmailsConfigured) return true;
        if (policy.DeniesAddress(from)) return true;
        if (recipients is null) return true;
        return recipients.Any(policy.DeniesAddress);
    }

    public static void EnsureMailVisible(
        ClientContext ctx,
        string tool,
        string? from,
        IEnumerable<string?>? recipients,
        RestrictedPolicy? policy = null)
    {
        if (!MailIsHidden(ctx, from, recipients, policy)) return;
        throw new McpException(
            $"Acceso denegado en '{tool}': el mensaje incluye una cuenta excluida o no se pudo verificar.");
    }

    public static void EnsureEventFacts(
        ClientContext ctx,
        string tool,
        string? organizer,
        IEnumerable<string?>? attendees,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return;
        policy ??= RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        if (EventIsHidden(ctx, organizer, attendees, policy))
        {
            throw new McpException(
                $"Acceso denegado en '{tool}': el evento incluye una cuenta excluida o no se pudo verificar.");
        }
    }

    public static async Task EnsureUserAsync(
        GraphService graph,
        ClientContext ctx,
        string tool,
        string? userId,
        string? authenticatedUserId)
    {
        if (!ctx.IsRestricted) return;
        var policy = RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);

        if (!string.IsNullOrWhiteSpace(authenticatedUserId) &&
            !string.Equals(authenticatedUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            await AssertUserAllowedAsync(graph, policy, tool, authenticatedUserId, authenticatedUserId);
        }

        await AssertUserAllowedAsync(graph, policy, tool, userId, authenticatedUserId);
    }

    public static async Task EnsureChatAsync(
        GraphService graph,
        ClientContext ctx,
        string tool,
        string chatId,
        string? authenticatedUserId)
    {
        if (!ctx.IsRestricted) return;
        var policy = RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        await EnsureActorAsync(graph, policy, tool, authenticatedUserId);

        try
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var members = await client.Chats[chatId].Members.GetAsync();
            if (members?.Value is null || members.Value.Count == 0)
                throw Deny(tool, "el chat no tiene miembros verificables");

            foreach (var member in members.Value)
            {
                if (!await MemberIsVisibleAsync(client, ctx, member, policy))
                    throw Deny(tool, "el chat incluye una cuenta excluida o no se pudo verificar");
            }
        }
        catch (McpException)
        {
            throw;
        }
        catch
        {
            throw Deny(tool, "no se pudo verificar el chat");
        }
    }

    public static async Task EnsureTeamAsync(
        GraphService graph,
        ClientContext ctx,
        string tool,
        string teamId,
        string? authenticatedUserId)
    {
        if (!ctx.IsRestricted) return;
        var policy = RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        await EnsureActorAsync(graph, policy, tool, authenticatedUserId);

        try
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var team = await client.Teams[teamId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "displayName"];
            });
            EnsureTeamFacts(ctx, tool, team?.DisplayName, team?.Id ?? teamId, policy);
        }
        catch (McpException)
        {
            throw;
        }
        catch
        {
            throw Deny(tool, "no se pudo verificar el equipo");
        }
    }

    public static List<T> FilterTeams<T>(
        ClientContext ctx,
        string tool,
        IEnumerable<T> teams,
        Func<T, string?> displayName,
        Func<T, string?> teamId)
    {
        if (!ctx.IsRestricted) return teams.ToList();
        var policy = RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        var list = teams.ToList();
        if (list.Any(t => string.IsNullOrWhiteSpace(displayName(t))))
            throw Deny(tool, "no se pudo verificar el nombre de un equipo");
        return list.Where(t => !policy.DeniesTeam(displayName(t), teamId(t))).ToList();
    }

    public static async Task<bool> MemberIsVisibleAsync(
        GraphServiceClient client,
        ClientContext ctx,
        ConversationMember member,
        RestrictedPolicy? policy = null)
    {
        if (!ctx.IsRestricted) return true;
        policy ??= RestrictedPolicy.FromEnvironment();
        if (!policy.EmailsConfigured) return false;

        try
        {
            var addresses = await ResolveMemberAddressesAsync(client, member);
            if (addresses is null) return false;
            return !addresses.Any(policy.DeniesAddress);
        }
        catch (McpException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adjunto cuyo contentUrl apunta a OneDrive/SharePoint. Sin URL (hosted content
    /// del propio chat) no hay drive que comprobar: el chat ya se validó.
    /// </summary>
    public static async Task EnsureFileOwnerAsync(
        GraphService graph,
        ClientContext ctx,
        string tool,
        string? contentUrl,
        string? authenticatedUserId)
    {
        if (!ctx.IsRestricted) return;
        var policy = RestrictedPolicy.FromEnvironment();
        EnsureConfigured(ctx, tool, policy);
        if (string.IsNullOrWhiteSpace(contentUrl)) return;

        if (!Uri.TryCreate(contentUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase))
        {
            throw Deny(tool, "no se pudo verificar el dueño del fichero");
        }

        try
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var shareId = "u!" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(contentUrl))
                .TrimEnd('=').Replace('/', '_').Replace('+', '-');
            var item = await client.Shares[shareId].DriveItem.GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "parentReference"];
            });
            var driveId = item?.ParentReference?.DriveId;
            if (string.IsNullOrWhiteSpace(driveId))
                throw Deny(tool, "no se pudo identificar el drive del fichero");

            var drive = await client.Drives[driveId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "owner"];
            });
            var ownerId = drive?.Owner?.User?.Id;
            if (string.IsNullOrWhiteSpace(ownerId))
                throw Deny(tool, "no se pudo identificar el dueño del fichero");

            await AssertUserAllowedAsync(graph, policy, tool, ownerId, authenticatedUserId);
        }
        catch (McpException)
        {
            throw;
        }
        catch
        {
            throw Deny(tool, "no se pudo verificar el dueño del fichero");
        }
    }

    private static async Task EnsureActorAsync(
        GraphService graph,
        RestrictedPolicy policy,
        string tool,
        string? authenticatedUserId)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId)) return;
        await AssertUserAllowedAsync(graph, policy, tool, authenticatedUserId, authenticatedUserId);
    }

    private static async Task AssertUserAllowedAsync(
        GraphService graph,
        RestrictedPolicy policy,
        string tool,
        string? userId,
        string? authenticatedUserId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw Deny(tool, "la cuenta no se pudo verificar");

        try
        {
            var client = await graph.GetClientAsync(authenticatedUserId);
            var user = await client.Users[userId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "mail", "userPrincipalName", "proxyAddresses"];
            });
            if (user is null ||
                policy.UserHasNoAddress(user.Mail, user.UserPrincipalName, user.ProxyAddresses) ||
                policy.DeniesUser(user.Mail, user.UserPrincipalName, user.ProxyAddresses))
            {
                throw Deny(tool, "la cuenta está excluida o no se pudo verificar");
            }
        }
        catch (McpException)
        {
            throw;
        }
        catch
        {
            throw Deny(tool, "la cuenta no se pudo verificar");
        }
    }

    private static async Task<IReadOnlyList<string>?> ResolveMemberAddressesAsync(
        GraphServiceClient client,
        ConversationMember member)
    {
        if (member is not AadUserConversationMember aad)
            return null;

        if (!string.IsNullOrWhiteSpace(aad.Email))
            return [aad.Email];

        if (string.IsNullOrWhiteSpace(aad.UserId))
            return null;

        var user = await client.Users[aad.UserId].GetAsync(cfg =>
        {
            cfg.QueryParameters.Select = ["mail", "userPrincipalName", "proxyAddresses"];
        });
        if (user is null) return null;

        var addresses = new List<string>();
        if (!string.IsNullOrWhiteSpace(user.Mail)) addresses.Add(user.Mail);
        if (!string.IsNullOrWhiteSpace(user.UserPrincipalName)) addresses.Add(user.UserPrincipalName);
        if (user.ProxyAddresses is not null)
        {
            foreach (var proxy in user.ProxyAddresses)
            {
                if (string.IsNullOrWhiteSpace(proxy)) continue;
                var colon = proxy.IndexOf(':');
                addresses.Add(colon >= 0 && colon < proxy.Length - 1 ? proxy[(colon + 1)..] : proxy);
            }
        }

        return addresses.Count == 0 ? null : addresses;
    }

    private static McpException Deny(string tool, string reason) =>
        new($"Acceso denegado en '{tool}': {reason}.");
}
