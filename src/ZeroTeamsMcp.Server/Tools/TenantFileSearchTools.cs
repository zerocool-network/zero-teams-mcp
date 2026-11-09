using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Search.Query;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Busca archivos y carpetas en los OneDrive y sitios del tenant con la
/// Search API. No descarga el archivo ni escribe nada.
/// </summary>
[McpServerToolType]
public sealed class TenantFileSearchTools
{
    [McpServerTool(Name = "search_tenant_files")]
    [Description("Busca un archivo o carpeta en todos los OneDrive y sitios de SharePoint del tenant, en una sola llamada. query es obligatorio y debe ser concreto, por ejemplo filename:informe.pdf. Una palabra suelta puede hacer que Graph agote el tiempo: ese error se devuelve y no se reintenta. fileType es la extensión y se aplica después, así que la página puede ser más corta que top. No descarga el archivo ni lo modifica. Para la carpeta de una persona usa onedrive_list_items.")]
    public static async Task<string> Search(
        GraphService graph,
        ILogger<TenantFileSearchTools> logger,
        ClientContext clientContext,
        [Description("Consulta KQL. Obligatoria. Usa filename:nombre o una frase concreta, no una palabra suelta.")] string query,
        [Description("Opcional. Extensión, con o sin punto. Se filtra aquí, no en Graph.")] string? fileType = null,
        [Description("Opcional. Máximo de resultados pedidos a Graph. Default 25, tope 50.")] int top = 25,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("search_tenant_files", authenticatedUserId, async () =>
        {
            if (!TenantFileSearch.TryRequest(query, top, out var body, out var message))
                return new { Error = "badRequest", Message = message };

            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var posted = await client.Search.Query.PostAsQueryPostResponseAsync(new QueryPostRequestBody
                {
                    Requests =
                    [
                        new SearchRequest
                        {
                            EntityTypes = [EntityType.DriveItem],
                            Query = new SearchQuery { QueryString = body.QueryString },
                            From = 0,
                            Size = body.Size,
                            Region = body.Region,
                            Fields =
                            [
                                "id", "name", "size", "webUrl", "lastModifiedDateTime",
                                "createdBy", "folder", "file"
                            ]
                        }
                    ]
                });

                var policy = RestrictedPolicy.FromEnvironment();
                var hits = posted?.Value?.FirstOrDefault()?.HitsContainers?.FirstOrDefault()?.Hits ?? [];
                return (object)hits.Select(hit => hit.Resource).OfType<DriveItem>()
                    .Select(item => new
                    {
                        Item = item,
                        Owner = OwnerEmail(item.CreatedBy?.User)
                    })
                    .Where(row => TenantFileSearch.MatchesFileType(row.Item.Name, fileType))
                    .Where(row => !TenantFileSearch.HideFromRestricted(
                        clientContext.IsRestricted, policy, row.Owner))
                    .Select(row => new
                    {
                        row.Item.Id,
                        row.Item.Name,
                        Kind = row.Item.Folder is not null ? "folder" : "file",
                        row.Item.Size,
                        row.Item.WebUrl,
                        row.Item.LastModifiedDateTime,
                        Owner = row.Owner
                    });
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 403)
            {
                return new
                {
                    Error = "accessDenied",
                    Message = "Falta permiso de lectura de archivos o sitios. No se cambia nada en Azure."
                };
            }
            catch (ODataError ex)
            {
                return new { Error = ex.Error?.Code ?? "graph", Message = ex.Error?.Message };
            }
        });
    }

    private static string? OwnerEmail(Identity? user)
    {
        if (user?.AdditionalData is null) return null;
        if (!user.AdditionalData.TryGetValue("email", out var raw) || raw is null) return null;
        if (raw is string text) return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (raw is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        return null;
    }
}
