using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Lectura de buzón y OneDrive. La key admin ve cualquier buzón. La key
/// restringida también, salvo las cuentas de la denylist: ni su buzón, ni su
/// OneDrive, ni un mensaje donde aparezcan.
/// </summary>
[McpServerToolType]
public sealed class MailDriveTools
{
    [McpServerTool(Name = "mail_list_messages")]
    [Description("ÚNICO tool para listar correo. No uses Outlook ni otro conector. Para un periodo pasa sinceDate y untilDate (yyyy-MM-dd, hora de Madrid). folder: inbox o sent (Enviados). fromAddress es el remitente. toAddress es el destinatario (Para, CC o CCO): email o dominio, también en Enviados. query no se ignora: una palabra busca asunto y cuerpo; varias palabras son una frase exacta en asunto y cuerpo, pásalas juntas. subject y hasAttachments acotan más. format: triage (barridos, mínimo), compact, full (legacy). includeQueryEcho=false omite eco de filtros. Si la respuesta trae before o nextPage, vuelve a llamar con ese valor hasta cubrir el periodo y reporta cobertura. top hasta 100. hasAttachments es false cuando el adjunto va incrustado en el cuerpo, como en muchos RV:. Para leer un adjunto usa mail_list_attachments y mail_read_attachment. No envía correo.")]
    public static async Task<string> ListMessages(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        [Description("Email del buzón.")] string userId,
        [Description("Opcional. Máximo de mensajes de esta página. Default 25, tope 100.")] int top = 25,
        [Description("Opcional. Inicio inclusive, yyyy-MM-dd.")] string? sinceDate = null,
        [Description("Opcional. Fin inclusive, yyyy-MM-dd.")] string? untilDate = null,
        [Description("Opcional. inbox, sent, drafts, deleted, junk, archive. Vacío = todo el buzón.")] string? folder = null,
        [Description("Opcional. Email del remitente.")] string? fromAddress = null,
        [Description("Opcional. Email o dominio del destinatario. Busca en Para, CC y CCO, también en Enviados.")] string? toAddress = null,
        [Description("Opcional. Texto libre. Una palabra busca en asunto y cuerpo. Varias palabras, juntas, son una frase exacta en asunto y cuerpo. No se ignora.")] string? query = null,
        [Description("Opcional. Texto que debe aparecer en el asunto.")] string? subject = null,
        [Description("Opcional. true = solo con adjuntos. false = solo sin adjuntos.")] bool? hasAttachments = null,
        [Description("Opcional. Cursor before de la respuesta anterior, para seguir hacia atrás.")] string? before = null,
        [Description("Opcional. Página, desde 1. Prefiere before.")] int page = 1,
        [Description("Opcional. compact|triage|full. Default full (legacy). triage minimiza tokens.")] string? format = null,
        [Description("Opcional. true incluye eco de filtros; default false.")] bool includeQueryEcho = false,
        [Description("Opcional. Longitud de preview en compact (0..280).")] int? previewChars = null,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        top = Math.Clamp(top, 1, MailListQuery.MaxTop);
        page = Math.Clamp(page, 1, MailListQuery.MaxPage);
        if (!TryMailListFormat(format, out var fmt, out var fmtError))
            return JsonSerializer.Serialize(new { Error = "format", Message = fmtError }, CompactJson);
        var pChars = previewChars.HasValue
            ? Math.Clamp(previewChars.Value, 0, 280)
            : fmt == MailListFormat.Triage ? 0 : fmt == MailListFormat.Compact ? 120 : 280;
        var jsonOpts = fmt == MailListFormat.Full ? (JsonSerializerOptions?)null : CompactJson;
        return await logger.ExecuteWithLogging("mail_list_messages", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "mail_list_messages", target, authenticatedUserId);
            if (!MailListQuery.TryFolder(folder, out var folderId, out var folderError))
                return (object)new { Error = "folder", Message = folderError };
            var dateField = MailListQuery.DateField(folderId);
            if (!MailListQuery.TryFilter(sinceDate, untilDate, dateField, out var filter, out var dateError, before))
                return (object)new { Error = "date", Message = dateError };
            var useSearch = !string.IsNullOrWhiteSpace(fromAddress)
                || !string.IsNullOrWhiteSpace(toAddress)
                || !string.IsNullOrWhiteSpace(query)
                || !string.IsNullOrWhiteSpace(subject)
                || hasAttachments is not null;
            string? search = null;
            if (useSearch)
            {
                if (!MailListQuery.TrySearch(sinceDate, untilDate, fromAddress, subject, hasAttachments, MailListQuery.SearchDateProperty(folderId), out search, out var searchError, toAddress, query))
                    return (object)new { Error = "search", Message = searchError };
                filter = null;
            }
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var current = folderId is null
                    ? await client.Users[target].Messages.GetAsync(cfg => Prepare(cfg, top, filter, search, dateField))
                    : await client.Users[target].MailFolders[folderId].Messages.GetAsync(cfg => Prepare(cfg, top, filter, search, dateField));
                for (var i = 1; i < page; i++)
                {
                    if (string.IsNullOrEmpty(current?.OdataNextLink))
                    {
                        current = null;
                        break;
                    }

                    var nextLink = current.OdataNextLink;
                    current = folderId is null
                        ? await client.Users[target].Messages.WithUrl(nextLink).GetAsync()
                        : await client.Users[target].MailFolders[folderId].Messages.WithUrl(nextLink).GetAsync();
                }

                var messages = current?.Value ?? [];
                if (clientContext.IsRestricted)
                {
                    messages = messages.Where(m => !RestrictedAccessGuard.MailIsHidden(
                        clientContext,
                        m.From?.EmailAddress?.Address,
                        Recipients(m.ToRecipients, m.CcRecipients))).ToList();
                }
                var oldest = messages
                    .Select(m => dateField == "sentDateTime" ? m.SentDateTime : m.ReceivedDateTime)
                    .Where(d => d is not null)
                    .Select(d => d!.Value)
                    .DefaultIfEmpty()
                    .Min();
                var nextPage = string.IsNullOrEmpty(current?.OdataNextLink) ? (int?)null : page + 1;
                var beforeCursor = messages.Count == 0 || oldest == default ? null : oldest.ToUniversalTime().ToString("o");
                var projected = messages.Select(m => ProjectMailMessage(m, fmt, dateField, pChars)).ToArray();

                if (!includeQueryEcho && fmt != MailListFormat.Full)
                {
                    return (object)new
                    {
                        Folder = folderId ?? "all",
                        Page = page,
                        Mode = useSearch ? "search" : "list",
                        Format = fmt.ToString().ToLowerInvariant(),
                        Count = projected.Length,
                        NextPage = nextPage,
                        Before = beforeCursor,
                        Messages = projected
                    };
                }

                return (object)new
                {
                    Folder = folderId ?? "all",
                    Page = page,
                    Mode = useSearch ? "search" : "list",
                    SinceDate = sinceDate,
                    UntilDate = untilDate,
                    FromAddress = fromAddress,
                    ToAddress = toAddress,
                    Query = query,
                    Subject = subject,
                    HasAttachments = hasAttachments,
                    NextPage = nextPage,
                    Before = beforeCursor,
                    Messages = projected
                };
            }
            catch (ODataError ex)
            {
                return (object)GraphError(ex);
            }
        }, jsonOpts);
    }

    [McpServerTool(Name = "mail_get_message")]
    [Description("ÚNICO tool para abrir un correo ya listado. No uses Outlook ni otro conector. Si hasAttachments es true, el texto del adjunto no viene aquí: usa mail_list_attachments y mail_read_attachment. bodyFormat: text (default legacy fase 1 sigue html por compat; usa text+maxBodyChars para ahorrar tokens), html o none. No envía correo.")]
    public static async Task<string> GetMessage(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        [Description("Email del buzón.")] string userId,
        [Description("Id del mensaje devuelto por mail_list_messages.")] string messageId,
        [Description("Opcional. text|html|none. Default html (legacy fase 1).")] string? bodyFormat = null,
        [Description("Opcional. Máximo de caracteres del cuerpo (0..8000). Default 8000 legacy; usa 2000 con text.")] int? maxBodyChars = null,
        [Description("Opcional. true incluye cabeceras/metadatos; default true.")] bool includeHeaders = true,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        if (!TryMailBodyFormat(bodyFormat, out var bfmt, out var bfmtError))
            return JsonSerializer.Serialize(new { Error = "bodyFormat", Message = bfmtError }, CompactJson);
        var maxChars = maxBodyChars.HasValue ? Math.Clamp(maxBodyChars.Value, 0, 8000) : 8000;
        var jsonOpts = bfmt == MailBodyFormat.Html ? (JsonSerializerOptions?)null : CompactJson;
        return await logger.ExecuteWithLogging("mail_get_message", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "mail_get_message", target, authenticatedUserId);
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var m = await client.Users[target].Messages[messageId].GetAsync(cfg =>
                {
                    var select = new List<string> { "id", "subject", "from", "receivedDateTime", "hasAttachments" };
                    if (includeHeaders)
                        select.AddRange(["toRecipients", "ccRecipients"]);
                    if (bfmt != MailBodyFormat.None)
                        select.Add("body");
                    cfg.QueryParameters.Select = select.ToArray();
                });
                RestrictedAccessGuard.EnsureMailVisible(
                    clientContext,
                    "mail_get_message",
                    m?.From?.EmailAddress?.Address,
                    Recipients(m?.ToRecipients, m?.CcRecipients));

                string? body = null;
                var bodyTruncated = false;
                var bodyFormatOut = bfmt.ToString().ToLowerInvariant();
                if (bfmt == MailBodyFormat.None)
                {
                    body = null;
                }
                else if (bfmt == MailBodyFormat.Text)
                {
                    (body, bodyTruncated) = BodyToText(m?.Body?.Content, maxChars);
                }
                else
                {
                    var raw = m?.Body?.Content;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        bodyTruncated = raw.Length > maxChars;
                        body = Trim(raw, maxChars);
                    }
                }

                if (!includeHeaders)
                {
                    return (object)new
                    {
                        m?.Id,
                        m?.Subject,
                        From = m?.From?.EmailAddress?.Address,
                        m?.HasAttachments,
                        Body = body,
                        BodyTruncated = bfmt == MailBodyFormat.None ? (bool?)null : bodyTruncated,
                        BodyFormat = bodyFormatOut
                    };
                }

                return (object)new
                {
                    m?.Id,
                    m?.Subject,
                    From = m?.From?.EmailAddress?.Address,
                    To = m?.ToRecipients?.Select(r => r.EmailAddress?.Address).Where(a => a is not null).ToArray(),
                    Cc = m?.CcRecipients?.Select(r => r.EmailAddress?.Address).Where(a => a is not null).ToArray(),
                    m?.ReceivedDateTime,
                    m?.HasAttachments,
                    Body = body,
                    BodyTruncated = bfmt == MailBodyFormat.None ? (bool?)null : bodyTruncated,
                    BodyFormat = bodyFormatOut
                };
            }
            catch (ODataError ex)
            {
                return (object)GraphError(ex);
            }
        }, jsonOpts);
    }

    [McpServerTool(Name = "mail_list_attachments")]
    [Description("Lista los adjuntos de un correo ya abierto. Devuelve id, nombre, tipo y tamaño. No baja el archivo. Para el texto usa mail_read_attachment. Para el archivo binario usa mail_download_attachment. No envía correo.")]
    public static async Task<string> ListAttachments(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        [Description("Email del buzón.")] string userId,
        [Description("Id del mensaje devuelto por mail_list_messages.")] string messageId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        return await logger.ExecuteWithLogging("mail_list_attachments", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "mail_list_attachments", target, authenticatedUserId);
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var message = await client.Users[target].Messages[messageId].GetAsync(cfg =>
                {
                    cfg.QueryParameters.Select = ["id", "from", "toRecipients", "ccRecipients"];
                });
                RestrictedAccessGuard.EnsureMailVisible(
                    clientContext,
                    "mail_list_attachments",
                    message?.From?.EmailAddress?.Address,
                    Recipients(message?.ToRecipients, message?.CcRecipients));
                var page = await client.Users[target].Messages[messageId].Attachments.GetAsync();
                return (object)(page?.Value ?? []).Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.ContentType,
                    item.Size,
                    IsInline = item.IsInline
                });
            }
            catch (ODataError ex)
            {
                return GraphError(ex);
            }
        });
    }

    [McpServerTool(Name = "mail_read_attachment")]
    [Description("Lee el texto de un adjunto de correo (PDF, DOCX, XLSX, PPTX, TXT, CSV o ZIP) por chunks. No descarga el binario al cliente ni envía correo. Usa offset/maxChars para paginar texto largo. Si es una imagen, lo dice y no inventa el contenido.")]
    public static async Task<string> ReadAttachment(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        [Description("Email del buzón.")] string userId,
        [Description("Id del mensaje.")] string messageId,
        [Description("Id del adjunto devuelto por mail_list_attachments.")] string attachmentId,
        [Description("Opcional. Desplazamiento en caracteres; default 0.")] int offset = 0,
        [Description("Opcional. Máximo de caracteres a devolver (1..20000); default 8000.")] int maxChars = 8000,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        offset = Math.Max(0, offset);
        maxChars = Math.Clamp(maxChars, 1, 20000);
        return await logger.ExecuteWithLogging("mail_read_attachment", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "mail_read_attachment", target, authenticatedUserId);
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var message = await client.Users[target].Messages[messageId].GetAsync(cfg =>
                {
                    cfg.QueryParameters.Select = ["id", "from", "toRecipients", "ccRecipients"];
                });
                RestrictedAccessGuard.EnsureMailVisible(
                    clientContext,
                    "mail_read_attachment",
                    message?.From?.EmailAddress?.Address,
                    Recipients(message?.ToRecipients, message?.CcRecipients));
                var attachment = await client.Users[target].Messages[messageId].Attachments[attachmentId].GetAsync();
                if (attachment is not Microsoft.Graph.Models.FileAttachment file || file.ContentBytes is null || file.ContentBytes.Length == 0)
                {
                    return (object)new
                    {
                        Error = "noContent",
                        attachment?.Name,
                        attachment?.ContentType,
                        attachment?.Size,
                        Message = "El adjunto no trae un archivo con texto. Si pesa más de unos megas, Graph no incluye el contenido en esta llamada."
                    };
                }

                if (file.ContentBytes.Length > 15 * 1024 * 1024)
                {
                    return (object)new
                    {
                        Error = "tooLarge",
                        file.Name,
                        file.ContentType,
                        file.Size,
                        Message = "El adjunto pasa de 15 MB y no se lee aquí."
                    };
                }

                var fullText = AttachmentText(file.Name, file.ContentType, file.ContentBytes) ?? "";
                var totalChars = fullText.Length;
                if (offset >= totalChars)
                {
                    return (object)new
                    {
                        file.Name,
                        file.ContentType,
                        file.Size,
                        Text = "",
                        Offset = offset,
                        ReturnedChars = 0,
                        NextOffset = (int?)null,
                        Truncated = false,
                        TotalChars = totalChars
                    };
                }
                var take = Math.Min(maxChars, totalChars - offset);
                var chunk = fullText.Substring(offset, take);
                var nextOffset = offset + take < totalChars ? (int?)(offset + take) : null;
                return (object)new
                {
                    file.Name,
                    file.ContentType,
                    file.Size,
                    Text = chunk,
                    Offset = offset,
                    ReturnedChars = chunk.Length,
                    NextOffset = nextOffset,
                    Truncated = nextOffset.HasValue,
                    TotalChars = totalChars
                };
            }
            catch (ODataError ex)
            {
                return (object)GraphError(ex);
            }
        });
    }

    [McpServerTool(Name = "mail_download_attachment")]
    [Description("Guarda un adjunto de correo en disco con el sistema operativo y devuelve solo name, contentType, size, path y url. No devuelve el archivo ni base64: no lo pegues en el chat. La url se baja con curl -o. Tope 25 MB. No envía correo.")]
    public static async Task<string> DownloadAttachment(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        IHttpContextAccessor httpAccessor,
        [Description("Email del buzón.")] string userId,
        [Description("Id del mensaje.")] string messageId,
        [Description("Id del adjunto devuelto por mail_list_attachments.")] string attachmentId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        return await logger.ExecuteWithLogging("mail_download_attachment", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "mail_download_attachment", target, authenticatedUserId);
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var message = await client.Users[target].Messages[messageId].GetAsync(cfg =>
                {
                    cfg.QueryParameters.Select = ["id", "from", "toRecipients", "ccRecipients"];
                });
                RestrictedAccessGuard.EnsureMailVisible(
                    clientContext,
                    "mail_download_attachment",
                    message?.From?.EmailAddress?.Address,
                    Recipients(message?.ToRecipients, message?.CcRecipients));
                var attachment = await client.Users[target].Messages[messageId].Attachments[attachmentId].GetAsync();
                if (attachment is not Microsoft.Graph.Models.FileAttachment file)
                {
                    return new
                    {
                        Error = "noContent",
                        attachment?.Name,
                        attachment?.ContentType,
                        attachment?.Size,
                        Message = "El adjunto no es un archivo."
                    };
                }

                if (file.Size is > MaxAttachmentBytes)
                {
                    return TooLarge(file.Name, file.ContentType, file.Size);
                }

                byte[]? bytes;
                if (file.ContentBytes is { Length: > 0 })
                {
                    bytes = file.ContentBytes;
                }
                else
                {
                    var downloaded = await ReadAttachmentBytesAsync(client, target, messageId, attachmentId);
                    if (downloaded.TooLarge)
                        return TooLarge(file.Name, file.ContentType, file.Size ?? MaxAttachmentBytes + 1L);
                    bytes = downloaded.Bytes;
                }

                if (bytes is null || bytes.Length == 0)
                {
                    return new
                    {
                        Error = "noContent",
                        file.Name,
                        file.ContentType,
                        file.Size,
                        Message = "Graph no devolvió el archivo."
                    };
                }

                if (bytes.Length > MaxAttachmentBytes)
                    return TooLarge(file.Name, file.ContentType, bytes.Length);

                var saved = MailAttachmentStore.Save(file.Name, file.ContentType, bytes);
                var url = MailAttachmentStore.UrlFor(httpAccessor.HttpContext?.Request, saved.Id);
                return (object)new
                {
                    saved.Name,
                    saved.ContentType,
                    saved.Size,
                    saved.Path,
                    Url = url
                };
            }
            catch (ODataError ex)
            {
                return GraphError(ex);
            }
        });
    }

    [McpServerTool(Name = "onedrive_list_items")]
    [Description("Lista ficheros y carpetas del OneDrive de un usuario. path vacío es la raíz. No modifica nada.")]
    public static async Task<string> ListItems(
        GraphService graph,
        ILogger<MailDriveTools> logger,
        ClientContext clientContext,
        [Description("Email del dueño del OneDrive.")] string userId,
        [Description("Opcional. Ruta dentro del drive, por ejemplo Documentos/Informes. Vacío = raíz.")] string? path = null,
        [Description("Opcional. Máximo de elementos. Default 25, tope 50.")] int top = 25,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        var target = clientContext.ResolveTarget(userId) ?? userId;
        top = Math.Clamp(top, 1, 50);
        return await logger.ExecuteWithLogging("onedrive_list_items", authenticatedUserId, async () =>
        {
            await RestrictedAccessGuard.EnsureUserAsync(
                graph, clientContext, "onedrive_list_items", target, authenticatedUserId);
            try
            {
                var client = await graph.GetClientAsync(authenticatedUserId);
                var drive = await client.Users[target].Drive.GetAsync(cfg =>
                {
                    cfg.QueryParameters.Select = ["id"];
                });
                if (string.IsNullOrWhiteSpace(drive?.Id))
                    return new { Error = "noDrive", Message = "El usuario no tiene un OneDrive." };

                var itemId = "root";
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var located = await client.Drives[drive.Id].Root
                        .ItemWithPath(path.Trim().Trim('/'))
                        .GetAsync(cfg => cfg.QueryParameters.Select = ["id"]);
                    if (string.IsNullOrWhiteSpace(located?.Id))
                        return new { Error = "pathNotFound", Message = "No existe esa ruta en el OneDrive." };
                    itemId = located.Id;
                }

                var page = await client.Drives[drive.Id].Items[itemId].Children.GetAsync(cfg =>
                {
                    cfg.QueryParameters.Top = top;
                    cfg.QueryParameters.Select = ["id", "name", "size", "folder", "file", "webUrl", "lastModifiedDateTime"];
                });
                return (object)(page?.Value ?? []).Select(item => new
                {
                    item.Id,
                    item.Name,
                    Kind = item.Folder is not null ? "folder" : "file",
                    item.Size,
                    item.WebUrl,
                    item.LastModifiedDateTime
                });
            }
            catch (ODataError ex)
            {
                return GraphError(ex);
            }
        });
    }

    internal static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal enum MailListFormat { Full, Compact, Triage }
    private enum MailBodyFormat { Html, Text, None }

    internal static bool TryMailListFormat(string? value, out MailListFormat format, out string? error)
    {
        error = null;
        format = MailListFormat.Full; // fase 1 legacy default
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "full": format = MailListFormat.Full; return true;
            case "compact": format = MailListFormat.Compact; return true;
            case "triage": format = MailListFormat.Triage; return true;
            default:
                error = "format debe ser compact, triage o full.";
                return false;
        }
    }

    private static bool TryMailBodyFormat(string? value, out MailBodyFormat format, out string? error)
    {
        error = null;
        format = MailBodyFormat.Html; // fase 1 legacy default
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "html": format = MailBodyFormat.Html; return true;
            case "text": format = MailBodyFormat.Text; return true;
            case "none": format = MailBodyFormat.None; return true;
            default:
                error = "bodyFormat debe ser text, html o none.";
                return false;
        }
    }

    private static string? TrimPlain(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value) || maxChars <= 0) return null;
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        if (normalized.Length == 0) return null;
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars];
    }

    private static (string? Text, bool Truncated) BodyToText(string? htmlOrText, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText) || maxChars <= 0) return (null, false);
        var s = htmlOrText;
        s = Regex.Replace(s, @"<\s*(script|style)[^>]*>.*?<\s*/\s*\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(p|div|li|tr|h[1-6]|table)\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
        if (s.Length == 0) return (null, false);
        var truncated = s.Length > maxChars;
        return (truncated ? s[..maxChars] : s, truncated);
    }

    internal static object ProjectMailMessage(
        Microsoft.Graph.Models.Message m,
        MailListFormat format,
        string? dateField,
        int previewChars)
    {
        var from = m.From?.EmailAddress?.Address;
        var msgDate = dateField == "sentDateTime" ? m.SentDateTime : m.ReceivedDateTime;
        if (format == MailListFormat.Triage)
        {
            return new
            {
                m.Id,
                m.Subject,
                From = from,
                Date = msgDate,
                m.HasAttachments
            };
        }

        var to = m.ToRecipients?.Select(r => r.EmailAddress?.Address).Where(a => a is not null).ToArray();
        var preview = format == MailListFormat.Compact ? TrimPlain(m.BodyPreview, previewChars) : Trim(m.BodyPreview, 280);
        if (format == MailListFormat.Compact)
        {
            return new
            {
                m.Id,
                m.Subject,
                From = from,
                To = to is { Length: > 0 } ? to : null,
                m.ReceivedDateTime,
                m.SentDateTime,
                m.HasAttachments,
                Preview = preview
            };
        }

        return new
        {
            m.Id,
            m.Subject,
            From = from,
            To = to,
            m.ReceivedDateTime,
            m.SentDateTime,
            m.HasAttachments,
            Preview = preview
        };
    }

    private static void Prepare(dynamic cfg, int top, string? filter, string? search, string dateField)
    {
        if (!string.IsNullOrEmpty(search))
            cfg.Headers.Add("ConsistencyLevel", "eventual");
        Configure(cfg.QueryParameters, top, filter, search, dateField);
    }

    private static void Configure(dynamic query, int top, string? filter, string? search, string dateField)
    {
        query.Top = top;
        query.Select = new[]
        {
            "id", "subject", "from", "toRecipients", "ccRecipients",
            "receivedDateTime", "sentDateTime", "hasAttachments", "bodyPreview"
        };
        if (!string.IsNullOrEmpty(search))
        {
            query.Search = search;
            return;
        }
        query.Orderby = new[] { $"{dateField} desc" };
        if (!string.IsNullOrEmpty(filter))
            query.Filter = filter;
    }

    internal static IEnumerable<string?> Recipients(
        IEnumerable<Microsoft.Graph.Models.Recipient>? to,
        IEnumerable<Microsoft.Graph.Models.Recipient>? cc)
    {
        foreach (var recipient in to ?? [])
            yield return recipient.EmailAddress?.Address;
        foreach (var recipient in cc ?? [])
            yield return recipient.EmailAddress?.Address;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max];
    }

    private const int MaxAttachmentBytes = 25 * 1024 * 1024;

    private static object TooLarge(string? name, string? contentType, long? size) => new
    {
        Error = "tooLarge",
        Name = name,
        ContentType = contentType,
        Size = size,
        Message = "El adjunto pasa de 25 MB y no se guarda."
    };

    private static async Task<(byte[]? Bytes, bool TooLarge)> ReadAttachmentBytesAsync(
        GraphServiceClient client,
        string userId,
        string messageId,
        string attachmentId)
    {
        var info = new RequestInformation
        {
            HttpMethod = Method.GET,
            URI = new Uri(
                "https://graph.microsoft.com/v1.0/users/"
                + Uri.EscapeDataString(userId)
                + "/messages/"
                + Uri.EscapeDataString(messageId)
                + "/attachments/"
                + Uri.EscapeDataString(attachmentId)
                + "/$value")
        };
        await using var stream = await client.RequestAdapter.SendPrimitiveAsync<Stream>(info);
        if (stream is null) return (null, false);

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) break;
            if (buffer.Length + read > MaxAttachmentBytes)
                return (null, true);
            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    private static string? AttachmentText(string? name, string? contentType, byte[] bytes)
    {
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return "Imagen adjunta. Este tool no describe imágenes.";

        var ext = Path.GetExtension(name ?? "").ToLowerInvariant();
        var text = ext switch
        {
            ".pdf" => string.Join("\n", TeamsFileTools.ExtractPdf(bytes).Pages),
            ".docx" => TeamsFileTools.ExtractDocxText(bytes),
            ".xlsx" => TeamsFileTools.ExtractXlsxText(bytes),
            ".pptx" => TeamsFileTools.ExtractPptxText(bytes),
            ".txt" or ".csv" or ".md" or ".json" or ".log" or ".xml" or ".html" or ".htm" => TeamsFileTools.ReadPlainText(bytes),
            ".zip" => TeamsFileTools.ZipStructureText(bytes) + "\n" + TeamsFileTools.ExtractZipText(bytes),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text))
            return "Sin texto extraíble para este tipo de adjunto.";
        return Trim(text, 50000);
    }

    private static object GraphError(ODataError ex) => new
    {
        Error = ex.Error?.Code ?? "graph",
        ex.Error?.Message
    };
}
