using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Directorio de la empresa. Solo lectura. La key restringida no ve las cuentas excluidas
/// ni una fila sin email.
/// </summary>
[McpServerToolType]
public sealed class DirectoryTools
{
    [McpServerTool(Name = "directory_search_users")]
    [Description("Busca personas en el directorio de la empresa por nombre o por email actual. Úsalo para saber la dirección de alguien antes de leer su buzón, sus chats o su OneDrive. No recorras buzones a ciegas. query es el nombre (varias palabras se exigen todas) o el email. No modifica nada.")]
    public static async Task<string> SearchUsers(
        GraphService graph,
        ILogger<DirectoryTools> logger,
        ClientContext clientContext,
        [Description("Nombre o email. Obligatorio.")] string query,
        [Description("Opcional. Máximo de personas. Default 50, tope 100.")] int top = 50,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("directory_search_users", authenticatedUserId, async () =>
        {
            if (!DirectoryUserQuery.TrySearch(query, out var search, out var message))
                return new { Error = "badRequest", Message = message };

            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var page = await client.Users.GetAsync(cfg =>
                {
                    cfg.Headers.Add("ConsistencyLevel", "eventual");
                    cfg.QueryParameters.Search = search;
                    cfg.QueryParameters.Select = ["displayName", "mail", "userPrincipalName"];
                    cfg.QueryParameters.Top = DirectoryUserQuery.ClampTop(top);
                    cfg.QueryParameters.Count = true;
                });
                return (object)Visible(clientContext, page?.Value);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 403)
            {
                return AccessDenied();
            }
            catch (ODataError ex)
            {
                return new { Error = ex.Error?.Code ?? "graph", Message = ex.Error?.Message };
            }
        });
    }

    [McpServerTool(Name = "directory_list_users")]
    [Description("Lista los buzones de la empresa, página a página. Si la respuesta trae next, vuelve a llamar con ese next hasta que no venga. Así cubres las direcciones que no conoces. No modifica nada.")]
    public static async Task<string> ListUsers(
        GraphService graph,
        ILogger<DirectoryTools> logger,
        ClientContext clientContext,
        [Description("Opcional. Máximo de esta página. Default 50, tope 100.")] int top = 50,
        [Description("Opcional. Valor next de la respuesta anterior.")] string? next = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("directory_list_users", authenticatedUserId, async () =>
        {
            if (!DirectoryUserQuery.IsGraphNext(next))
                return new { Error = "badRequest", Message = "next tiene que ser un enlace https de graph.microsoft.com." };

            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var page = string.IsNullOrWhiteSpace(next)
                    ? await client.Users.GetAsync(cfg =>
                    {
                        cfg.QueryParameters.Select = ["displayName", "mail", "userPrincipalName"];
                        cfg.QueryParameters.Top = DirectoryUserQuery.ClampTop(top);
                        cfg.QueryParameters.Orderby = ["displayName"];
                    })
                    : await client.Users.WithUrl(next).GetAsync();
                return (object)new
                {
                    Next = page?.OdataNextLink,
                    Users = Visible(clientContext, page?.Value)
                };
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 403)
            {
                return AccessDenied();
            }
            catch (ODataError ex)
            {
                return new { Error = ex.Error?.Code ?? "graph", Message = ex.Error?.Message };
            }
        });
    }

    private static object AccessDenied() => new
    {
        Error = "accessDenied",
        Message = "Falta permiso de lectura del directorio. No se cambia nada en Azure."
    };

    private static object[] Visible(ClientContext clientContext, IEnumerable<Microsoft.Graph.Models.User>? users)
    {
        var policy = RestrictedPolicy.FromEnvironment();
        return (users ?? [])
            .Where(user => !DirectoryUserQuery.HideFromRestricted(
                clientContext.IsRestricted, policy, user.Mail, user.UserPrincipalName))
            .Select(user => (object)new
            {
                user.DisplayName,
                Mail = user.Mail ?? user.UserPrincipalName
            })
            .ToArray();
    }
}
