using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Lectura de imágenes de un chat de Teams (capturas con flechas/círculos/anotaciones) SIN modelos
/// externos: la herramienta las descarga de Graph y las devuelve como content blocks MCP para que el
/// modelo cliente (multimodal) las lea él mismo.
///
/// Shape verificado 2026-10-05 (tenant Optima, chat 19:9b04bb1…@thread.v2, msgs 1791198527412 /
/// 1791198404576 / 1791198162075 / 1791195756602): attachments = [] SIEMPRE; la imagen solo aparece en
/// body.content como &lt;img src="…/hostedContents/{base64Id}/$value" width="357" height="130"&gt;.
/// hostedContents devuelve 1 item con contentType = null (igual que en audio), por lo que filtrar por
/// image/* sobre hostedContents NO sirve. La descarga de ese src con token app-only funciona (200 +
/// PNG real), sin scopes extra.
/// </summary>
[McpServerToolType]
public sealed class TeamsImageTools
{
    // Bytes ya descargados; se pierde al reiniciar el proceso.
    private static readonly ConcurrentDictionary<string, CachedImage> Cache = new();

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    // Capturas de Teams son de KBs; el tope solo evita mandarle un blob enorme al cliente MCP.
    private const long MaxImageBytes = 8 * 1024 * 1024;

    private const string SuggestedPrompt =
        "Describe en español qué señala cualquier flecha, círculo o anotación manual en esta captura de pantalla de una aplicación web, " +
        "y transcribe literalmente cualquier texto visible en la interfaz. Si no hay anotaciones, dilo. No inventes texto que no se lea.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed record CachedImage(string MessageId, string MimeType, byte[] Bytes, int? Width, int? Height);

    public sealed record ImageCandidate(string Source, string? Url, string? HostedContentId, string? ContentType, string? Name);

    public sealed record ImageInfo(int Index, string Source, int? Width, int? Height, string? ContentType);

    public sealed record ImageRow(
        string MessageId,
        string? From,
        string? FromUserId,
        DateTimeOffset? CreatedDateTime,
        int ImageCount,
        List<ImageInfo> Images,
        string Excerpt);

    /// <summary>
    /// Lista los mensajes de un chat que contienen imágenes, para saber qué capturas hay (y de quién)
    /// antes de descargar ninguna. El escaneo no descarga bytes ni llama a hostedContents: lee body HTML
    /// y attachments, así es barato aunque escanee muchas páginas. Al final solo descarga las imágenes
    /// de las filas devueltas (top) para rellenar contentType sniffando el MIME real de los bytes.
    /// </summary>
    [McpServerTool(Name = "teams_list_chat_images")]
    [Description("Lista los mensajes de un chat que contienen imágenes (capturas de pantalla), con remitente, fecha, nº de imágenes, contentType real (sniffeado descargando solo las filas devueltas), dimensiones declaradas y extracto de texto. Filtro opcional por remitente (p. ej. 'Fran,Noe,Luis,Miguel,Maruan') y desde fecha. Solo lectura. Úsalo antes de teams_get_image_description para elegir qué mensaje leer.")]
    public static async Task<string> ListChatImages(
        GraphService graph,
        ILogger<TeamsImageTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("Opcional. CSV de nombres de remitente a incluir (substring, insensible a mayúsculas). Ej: 'Fran,Noe,Luis,Miguel,Maruan'.")] string? fromNames = null,
        [Description("Opcional. Fecha ISO (yyyy-MM-dd): solo mensajes desde esa fecha.")] string? sinceDate = null,
        [Description("Opcional. Máximo de mensajes con imágenes a retornar. Default: 20.")] int top = 20,
        [Description("Opcional. Máximo de páginas de Graph a recorrer. Default: 20.")] int maxPages = 20,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging("teams_list_chat_images", authenticatedUserId, async () =>
        {
            var names = SplitNames(fromNames);
            DateTimeOffset? since = string.IsNullOrWhiteSpace(sinceDate)
                ? null
                : DateTimeOffset.Parse(sinceDate).ToUniversalTime();

            var client = await graph.GetClientAsync(authenticatedUserId);
            var pageSize = Math.Min(Math.Max(top, 1), 50);

            var pairs = new List<(ImageRow Row, List<ImageCandidate> Candidates)>();
            var scanned = 0;
            var pagesFetched = 0;
            var more = false;

            var page = await client.Chats[chatId].Messages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = pageSize;
            });

            while (page is not null)
            {
                pagesFetched++;

                if (page.Value is not null)
                {
                    foreach (var m in page.Value)
                    {
                        scanned++;
                        if (ShouldExclude(m.From?.User?.DisplayName, names, m.CreatedDateTime, since)) continue;
                        var pr = ProjectImageRowWithCandidates(m);
                        if (pr is not null) pairs.Add(pr.Value);
                    }
                }

                var oldestInPage = page.Value?.Count > 0 ? page.Value[^1].CreatedDateTime : null;
                var reachedTarget = pairs.Count >= top;
                var reachedDate = since is not null && oldestInPage is not null && oldestInPage <= since;
                var noMorePages = string.IsNullOrEmpty(page.OdataNextLink);

                if (reachedTarget || reachedDate || noMorePages || pagesFetched >= maxPages)
                {
                    more = !reachedTarget && !reachedDate && !noMorePages;
                    break;
                }

                page = await client.Chats[chatId].Messages.WithUrl(page.OdataNextLink!).GetAsync();
            }

            var rows = await ResolveContentTypesAsync(client, chatId, pairs.Take(top).ToList(), logger);

            return new
            {
                ChatId = chatId,
                Count = rows.Count,
                Rows = rows,
                Scanned = scanned,
                PagesFetched = pagesFetched,
                More = more,
                Filter = new
                {
                    FromNames = names,
                    SinceDate = since?.ToString("yyyy-MM-dd")
                }
            };
        });
    }

    /// <summary>
    /// Rellena ImageInfo.ContentType de las filas devueltas descargando (solo esas) y sniffando el
    /// MIME de los bytes. Concurrencia baja (3) y 1 reintento con pausa para tolerar 429 de Graph;
    /// un fallo individual deja el contentType en null y solo loguea warning — nunca rompe el listado.
    /// </summary>
    public static async Task<List<ImageRow>> ResolveContentTypesAsync(
        GraphServiceClient client,
        string chatId,
        List<(ImageRow Row, List<ImageCandidate> Candidates)> pairs,
        ILogger logger)
    {
        if (pairs.Count == 0) return [];

        using var gate = new SemaphoreSlim(3);
        var tasks = new Task<ImageRow>[pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
        {
            var (row, candidates) = pairs[i];
            tasks[i] = Task.Run(async () =>
            {
                var sniffed = new string?[candidates.Count];
                for (var j = 0; j < candidates.Count; j++)
                {
                    var c = candidates[j];
                    if (!string.IsNullOrEmpty(c.ContentType)) continue;
                    await gate.WaitAsync();
                    try
                    {
                        sniffed[j] = await SniffContentTypeAsync(logger, client, chatId, row.MessageId, c);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
                return ApplySniffedContentTypes(row, candidates, sniffed);
            });
        }

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>Descarga y sniffa el MIME; reintenta con backoff (1.5 s, luego 3 s) ante 429/5xx. null = siguió fallando.</summary>
    private static async Task<string?> SniffContentTypeAsync(
        ILogger logger, GraphServiceClient client, string chatId, string messageId, ImageCandidate c)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var bytes = await TeamsAttachmentTools.DownloadAsync(client, chatId, messageId, c.Url, c.HostedContentId);
                return SniffMimeType(bytes);
            }
            catch (Exception ex) when (attempt < 3)
            {
                logger.LogDebug(ex, "Fallo {Attempt} sniffing contentType de {Target} en mensaje {MessageId}; reintento con backoff",
                    attempt, c.Url ?? c.HostedContentId ?? "?", messageId);
                await Task.Delay(attempt == 1 ? 1500 : 3000);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo sniffear contentType de {Target} en mensaje {MessageId}",
                    c.Url ?? c.HostedContentId ?? "?", messageId);
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Devuelve la fila con contentType aplicado: solo llena los vacíos con el valor sniffeado del
    /// candidato de la misma posición (los ya conocidos se conservan). Si no hay nada nuevo, devuelve
    /// la misma fila.
    /// </summary>
    public static ImageRow ApplySniffedContentTypes(ImageRow row, List<ImageCandidate> candidates, string?[] sniffed)
    {
        if (sniffed.All(s => string.IsNullOrEmpty(s))) return row;

        var infos = new List<ImageInfo>(row.Images.Count);
        for (var i = 0; i < row.Images.Count; i++)
        {
            var info = row.Images[i];
            var ct = info.ContentType;
            if (string.IsNullOrEmpty(ct) && i < candidates.Count && i < sniffed.Length && !string.IsNullOrEmpty(sniffed[i]))
                ct = sniffed[i];
            infos.Add(new ImageInfo(info.Index, info.Source, info.Width, info.Height, ct));
        }
        return row with { Images = infos };
    }

    /// <summary>
    /// Descarga la imagen de un mensaje y la devuelve como content block (texto + imagen en base64)
    /// para que el modelo cliente la lea. Cero llamadas a modelos externos. Cachea los bytes por
    /// mensaje+URL, así que repetir la lectura no vuelve a descargar.
    /// </summary>
    [McpServerTool(Name = "teams_get_image_description")]
    [Description("Descarga la imagen (captura de pantalla) de un mensaje de Teams y la devuelve como imagen MCP para que la leas tú: texto visible de la interfaz, flechas, círculos y anotaciones manuales. No usa ningún modelo externo. Requiere chatId y messageId; 'index' elige cuál si el mensaje tiene varias. Devuelve un bloque de texto con metadatos y un prompt sugerido, y un bloque de imagen. Si falla, bloque de texto con diagnóstico JSON.")]
    public static async Task<IList<ContentBlock>> GetImageDescription(
        GraphService graph,
        ILogger<TeamsImageTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("ID del mensaje que contiene la imagen.")] string messageId,
        [Description("Opcional. Índice de la imagen si el mensaje tiene varias (0-based). Default: 0.")] int index = 0,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithContentLogging<IList<ContentBlock>>("teams_get_image_description", authenticatedUserId, async () =>
        {
            var attempts = new List<object>();
            var client = await graph.GetClientAsync(authenticatedUserId);

            ChatMessage? message = null;
            try
            {
                message = await client.Chats[chatId].Messages[messageId].GetAsync();
            }
            catch (Exception ex)
            {
                attempts.Add(TeamsAttachmentTools.Attempt("get message", messageId, ex));
                return Failure(messageId, "No se pudo leer el mensaje (¿chatId/messageId incorrectos o sin permiso Chat.ReadWrite.All?).",
                    null, null, [], attempts, authenticatedUserId, index);
            }

            if (message is null)
            {
                return Failure(messageId, "Graph no devolvió ese mensaje (posiblemente no existe en ese chat).",
                    null, null, [], attempts, authenticatedUserId, index);
            }

            List<ChatMessageHostedContent>? hosted = null;
            try
            {
                hosted = (await client.Chats[chatId].Messages[messageId].HostedContents.GetAsync())?.Value;
            }
            catch (Exception ex)
            {
                attempts.Add(TeamsAttachmentTools.Attempt("list hostedContents", null, ex));
            }

            var candidates = FindImageCandidates(message?.Attachments, hosted, message?.Body?.Content);
            if (candidates.Count == 0)
            {
                return Failure(messageId, "No se encontró ninguna imagen en el mensaje (ni en attachments, ni en hostedContents, ni como <img> en el body).",
                    message, hosted, candidates, attempts, authenticatedUserId, index);
            }

            if (index < 0 || index >= candidates.Count)
            {
                return Failure(messageId,
                    $"Índice {index} fuera de rango: el mensaje tiene {candidates.Count} imagen(es) (0..{candidates.Count - 1}).",
                    message, hosted, candidates, attempts, authenticatedUserId, index);
            }

            var candidate = candidates[index];
            var cacheKey = $"{chatId}|{messageId}|{candidate.Url ?? candidate.HostedContentId}";
            var cached = Cache.TryGetValue(cacheKey, out var image);

            if (!cached)
            {
                byte[] bytes;
                try
                {
                    bytes = await TeamsAttachmentTools.DownloadAsync(client, chatId, messageId, candidate.Url, candidate.HostedContentId);
                }
                catch (Exception ex)
                {
                    attempts.Add(TeamsAttachmentTools.Attempt($"download ({candidate.Source})", candidate.Url ?? candidate.HostedContentId, ex));
                    return Failure(messageId, $"No se pudo descargar la imagen ({candidate.Source}).",
                        message, hosted, candidates, attempts, authenticatedUserId, index);
                }

                if (bytes.Length == 0)
                {
                    return Failure(messageId, "Graph devolvió la imagen vacía.",
                        message, hosted, candidates, attempts, authenticatedUserId, index);
                }

                var dims = ImageDimensions(bytes);
                image = new CachedImage(messageId, SniffMimeType(bytes), bytes, dims?.Width, dims?.Height);
                Cache[cacheKey] = image;
            }

            return BuildContentBlocks(message, candidate, candidates.Count, index, image!, cached);
        });
    }

    /// <summary>
    /// Heurística tolerante para localizar las imágenes de un mensaje. Mismo criterio que
    /// FindAudioCandidates: primero la fuente real (body HTML), luego attachments, luego
    /// hostedContents con tipo image/*, y por último hostedContents de tipo desconocido si el body
    /// es un &lt;attachment&gt; suelto.
    /// </summary>
    public static List<ImageCandidate> FindImageCandidates(
        IEnumerable<ChatMessageAttachment>? attachments,
        IEnumerable<ChatMessageHostedContent>? hosted,
        string? bodyHtml)
    {
        var result = new List<ImageCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string source, string? url, string? hostedId, string? contentType, string? name)
        {
            if (url is not null && !seen.Add(url)) return;
            if (url is null && hostedId is not null && !seen.Add(hostedId)) return;
            result.Add(new ImageCandidate(source, url, hostedId, contentType, name));
        }

        // 1) Fuente real en Teams: <img src> dentro del body HTML.
        foreach (var url in TeamsAttachmentTools.ImageSourcesInHtml(bodyHtml))
        {
            Add("body<img src>", TeamsAttachmentTools.StripQuery(url), null, GuessContentType(url), null);
        }

        // 2) attachments con contentType image/* o extensión de imagen.
        foreach (var a in attachments ?? [])
        {
            var ct = a.ContentType ?? "";
            var looksImage = ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || ct.Contains("image", StringComparison.OrdinalIgnoreCase)
                || ct.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                || HasImageExtension(a.ContentUrl) || HasImageExtension(a.Name);

            if (!looksImage) continue;

            if (!string.IsNullOrEmpty(a.ContentUrl)) Add("attachment.contentUrl", a.ContentUrl, null, ct, a.Name);

            foreach (var url in TeamsAttachmentTools.UrlsInJson(a.Content))
            {
                if (HasImageExtension(url) || bodyHtml?.Contains(url, StringComparison.OrdinalIgnoreCase) != true)
                    Add("attachment.content", url, null, ct, a.Name);
            }
        }

        // 3) hostedContents con contentType image/* explícito (no pasa con estos tenants, pero es inofensivo).
        foreach (var h in hosted ?? [])
        {
            if (h.Id is not null && (h.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                Add("hostedContent", null, h.Id, h.ContentType, null);
            }
        }

        // 4) Fallback: mensaje sin <img> ni candidatos; hostedContents sin tipo conocido (o image/*) son la imagen.
        var bodyHasImg = bodyHtml?.Contains("<img", StringComparison.OrdinalIgnoreCase) == true;
        if (result.Count == 0 && !bodyHasImg)
        {
            foreach (var h in hosted ?? [])
            {
                if (h.Id is null) continue;
                var ct = h.ContentType ?? "";
                if (ct.Length == 0 || ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    Add("hostedContent (tipo desconocido)", null, h.Id, h.ContentType, null);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Proyecta un mensaje en su fila de imágenes (para teams_list_chat_images). Devuelve null si el
    /// mensaje no tiene ninguna imagen.
    /// </summary>
    public static ImageRow? ProjectImageRow(ChatMessage m) => ProjectImageRowWithCandidates(m)?.Row;

    /// <summary>Igual que ProjectImageRow pero conserva los candidatos (para sniffear contentType luego sin recomputar).</summary>
    public static (ImageRow Row, List<ImageCandidate> Candidates)? ProjectImageRowWithCandidates(ChatMessage m)
    {
        var candidates = FindImageCandidates(m.Attachments, null, m.Body?.Content);
        if (candidates.Count == 0) return null;

        var row = new ImageRow(
            m.Id ?? "",
            m.From?.User?.DisplayName,
            m.From?.User?.Id,
            m.CreatedDateTime,
            candidates.Count,
            DescribeImages(candidates, m.Body?.Content),
            Excerpt(m.Body?.Content));
        return (row, candidates);
    }

    /// <summary>Dimensiones declaradas en la etiqueta &lt;img&gt; por cada candidato (null si no aplican).</summary>
    public static List<ImageInfo> DescribeImages(List<ImageCandidate> candidates, string? bodyHtml)
    {
        var dims = new Dictionary<string, (int? Width, int? Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TeamsAttachmentTools.ImageTagsInHtml(bodyHtml))
        {
            var key = TeamsAttachmentTools.StripQuery(t.Url);
            if (!dims.ContainsKey(key)) dims[key] = (t.Width, t.Height);
        }

        var list = new List<ImageInfo>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            (int? w, int? h) = (null, null);
            if (c.Url is not null && dims.TryGetValue(TeamsAttachmentTools.StripQuery(c.Url), out var d))
            {
                (w, h) = d;
            }
            list.Add(new ImageInfo(i, c.Source, w, h, c.ContentType));
        }
        return list;
    }

    /// <summary>true = el mensaje queda fuera del filtro (remitente o fecha).</summary>
    public static bool ShouldExclude(string? fromDisplayName, string[] names, DateTimeOffset? created, DateTimeOffset? since)
    {
        if (since is not null && (created is null || created < since)) return true;
        if (names.Length == 0) return false;
        return fromDisplayName is null || !names.Any(n => fromDisplayName.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    public static string[] SplitNames(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Texto del body sin etiquetas HTML, colapsado a un extracto legible.</summary>
    public static string Excerpt(string? html, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var ch in html)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }

        var text = WebUtility.HtmlDecode(sb.ToString());
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max] + "…";
    }

    public static (int Width, int Height)? ImageDimensions(byte[] bytes) =>
        PngDimensions(bytes) ?? JpegDimensions(bytes) ?? GifDimensions(bytes);

    public static (int Width, int Height)? PngDimensions(byte[] png)
    {
        // IHDR es siempre el primer chunk tras la firma de 8 bytes.
        if (png.Length < 24) return null;
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!png.AsSpan(0, 8).SequenceEqual(sig)) return null;
        var w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    public static (int Width, int Height)? JpegDimensions(byte[] j)
    {
        if (j.Length < 4 || j[0] != 0xFF || j[1] != 0xD8) return null;
        var i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF) { i++; continue; }
            var marker = j[i + 1];
            // SOFn (excepto DHT/DAC/RST): el frame header lleva altura y ancho.
            if (marker >= 0xC0 && marker <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                var h = (j[i + 5] << 8) | j[i + 6];
                var w = (j[i + 7] << 8) | j[i + 8];
                return (w, h);
            }
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            var len = (j[i + 2] << 8) | j[i + 3];
            i += 2 + Math.Max(len, 2);
        }
        return null;
    }

    public static (int Width, int Height)? GifDimensions(byte[] g)
    {
        if (g.Length < 10) return null;
        if (g[0] != 'G' || g[1] != 'I' || g[2] != 'F') return null;
        return (g[6] | (g[7] << 8), g[8] | (g[9] << 8));
    }

    /// <summary>
    /// Muchos modelos de visión reescalan imágenes grandes y el texto pequeño queda borroso.
    /// Lo avisamos en vez de ocultarlo.
    /// </summary>
    public static string? OversizeNote(byte[] image)
    {
        var dims = ImageDimensions(image);
        if (dims is null) return null;
        var (w, h) = dims.Value;
        return w > 1120 || h > 1120
            ? $"La imagen mide {w}x{h}px; supera los 1120px en un lado, así que varios modelos de visión la reescalan y el texto pequeño puede salir borroso."
            : null;
    }

    public static List<ContentBlock> BuildContentBlocks(
        ChatMessage? message,
        ImageCandidate candidate,
        int total,
        int index,
        CachedImage image,
        bool cached)
    {
        var from = message?.From?.User?.DisplayName ?? "desconocido";
        var created = message?.CreatedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "sin fecha";
        var dims = image.Width is not null && image.Height is not null
            ? $"{image.Width}x{image.Height}px"
            : "dimensiones desconocidas";

        var sb = new StringBuilder();
        sb.Append("Imagen ").Append(index + 1).Append('/').Append(total)
          .Append(" del mensaje ").Append(message?.Id)
          .Append(" de ").Append(from)
          .Append(" (").Append(created).Append("). ")
          .Append(image.MimeType).Append(", ").Append(image.Bytes.Length).Append(" bytes, ").Append(dims).Append(". ")
          .Append("Origen: ").Append(candidate.Source).Append(". ")
          .Append(cached ? "Bytes servidos de caché (no se ha vuelto a descargar). " : "");
        var note = OversizeNote(image.Bytes);
        if (note is not null) sb.Append("Aviso: ").Append(note).Append(' ');
        sb.Append("\n\nPrompt sugerido: ").Append(SuggestedPrompt);

        if (image.Bytes.Length > MaxImageBytes)
        {
            return [new TextContentBlock
            {
                Text = $"La imagen pesa {image.Bytes.Length} bytes y supera el tope de {MaxImageBytes}; no se adjunta. " + sb
            }];
        }

        return
        [
            new TextContentBlock { Text = sb.ToString() },
            new ImageContentBlock { MimeType = image.MimeType, Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(image.Bytes)) }
        ];
    }

    private static List<ContentBlock> Failure(
        string messageId,
        string reason,
        ChatMessage? message,
        List<ChatMessageHostedContent>? hosted,
        List<ImageCandidate> candidates,
        List<object> attempts,
        string? authenticatedUserId,
        int index)
    {
        var payload = new
        {
            MessageId = messageId,
            Description = (string?)null,
            Index = index,
            Reason = reason,
            Diagnostics = new
            {
                RequestedAuth = authenticatedUserId is null ? "app-only" : "delegated (GraphService cae a app-only si falla el token delegado)",
                BodyContent = message?.Body?.Content,
                Attachments = message?.Attachments?.Select(a => new { a.Id, a.ContentType, a.ContentUrl, a.Name, a.Content, a.ThumbnailUrl }),
                HostedContents = hosted?.Select(h => new { h.Id, h.ContentType }),
                Candidates = candidates,
                Attempts = attempts
            }
        };

        return [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOpts) }];
    }

    private static string SniffMimeType(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 7 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 3 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return "image/gif";
        if (b.Length > 11 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B') return "image/webp";
        return "image/png";
    }

    private static string? GuessContentType(string? url)
    {
        if (!HasImageExtension(url)) return null;
        return TeamsAttachmentTools.StripQuery(url!).Split('.').Last().ToLowerInvariant() switch
        {
            "jpg" => "image/jpeg",
            var e => "image/" + e
        };
    }

    private static bool HasImageExtension(string? s) =>
        !string.IsNullOrEmpty(s) && ImageExtensions.Any(e => TeamsAttachmentTools.StripQuery(s).EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
