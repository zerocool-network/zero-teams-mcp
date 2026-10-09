using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UglyToad.PdfPig;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Lectura del contenido de los ficheros adjuntos a un mensaje de Teams (PDF, DOCX, XLSX, PPTX,
/// TXT/CSV). DownloadAsync baja el archivo autenticado contra Graph; el texto se extrae en local:
/// PdfPig para PDF digitales, OpenXML para Office, lectura directa para texto plano. PDF escaneado
/// (sin texto extraíble) se degrada a una imagen por página vía pdftoppm (poppler), que el modelo
/// cliente multimodal lee, igual que hace TeamsVideoTools con los frames.
///
/// Shape verificado 2026-10-08 (tenant Optima, chat 19:9b04bb1…@thread.v2, app-only): los ficheros
/// llegan como attachments[].contentType='reference' con name=nombre real y contentUrl=URL directa
/// OneDrive (personal/…/Documents/"Archivos de chat de Microsoft Teams"/…). NO son hostedContents.
/// Descargar esos contentUrl exige Files.Read.All/Sites.Read.All en el token (admin consent para
/// app-only, o auth delegada). /shares/{u!base64(url)}/driveItem devuelve 403 accessDenied sin esos
/// permisos — la herramienta lo reporta en el diagnóstico.
/// </summary>
[McpServerToolType]
public sealed class TeamsFileTools
{
    // Bytes ya descargados; caché LRU acotada a 200MB, se pierde al reiniciar el proceso.
    private static readonly LruByteCache Cache = new(MaxCacheBytes);

    private static readonly string[] DocumentExtensions =
        [".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv", ".md", ".json", ".log"];

    private static readonly string[] MediaExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff",
         ".mp4", ".mov", ".webm", ".m4v", ".avi", ".mkv",
         ".mp3", ".m4a", ".wav", ".ogg", ".opus"];

    // Tope anti-blob gigante (los informes reales son <10MB).
    private const long MaxFileBytes = 50 * 1024 * 1024;

    // Presupuesto total de la caché LRU de ficheros descargados.
    private const long MaxCacheBytes = 200L * 1024 * 1024;

    // PDF escaneado → páginas como imágenes.
    private const int MaxPdfPages = 12;

    // Germen de "página escaneada": un PDF digital suele tener texto; si saca muy poco, se degrada.
    private const int MinPdfTextChars = 80;

    // Límites de extracción (las hojas/PDFs reales usan capas de resumen conservadoras).
    private const int MaxXlsxRows = 500;
    private const int MaxXlsxCells = 20_000;

    // Recapara el bloque de texto devuelto para no ahogar al modelo.
    private const int MaxOutputChars = 120_000;
    private const int ProcessTimeoutMs = 120_000;

    private static readonly string[] PdfPageExts = { ".pdf" };

    private const string SuggestedPrompt =
        "Resume en español el contenido de este documento adjunto: señala el propósito, la estructura " +
        "(secciones/páginas/hojas) y los datos o cifras relevantes. Si es una imagen de página " +
        "(PDF escaneado), transcribe la información visible. Si el texto está cortado o ilegible, dilo " +
        "en vez de inventar.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed record CachedFile(string MimeType, byte[] Bytes);

    public sealed record FileCandidate(string Source, string? Url, string? Name, string? ContentType);

    public sealed record PdfExtraction(List<string> Pages, bool HasExtractableText);

    [McpServerTool(Name = "teams_get_file_content")]
    [Description("Descarga el fichero adjunto de un mensaje de Teams (PDF, DOCX, XLSX, PPTX, TXT/CSV) y devuelve su texto: por páginas en PDF, por hojas en XLSX, por diapositiva en PPTX, tal cual en texto plano. Si el PDF es escaneado (sin texto), degrada a una imagen por página para que tú (modelo cliente multimodal) lo leas. Devuelve un bloque de texto con metadatos y contenido; en PDF escaneado, además, bloques de imagen. Si falla o exige permisos que faltan, bloque de texto con diagnóstico JSON.")]
    public static async Task<IList<ContentBlock>> GetFileContent(
        GraphService graph,
        ILogger<TeamsFileTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("ID del mensaje que contiene el fichero.")] string messageId,
        [Description("Opcional. Índice del fichero si el mensaje tiene varios (0-based). Default: 0.")] int index = 0,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithContentLogging<IList<ContentBlock>>("teams_get_file_content", authenticatedUserId, async () =>
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
                    null, [], attempts, authenticatedUserId, index);
            }

            if (message is null)
            {
                return Failure(messageId, "Graph no devolvió ese mensaje (posiblemente no existe en ese chat).",
                    null, [], attempts, authenticatedUserId, index);
            }

            var candidates = FindFileCandidates(message.Attachments);
            if (candidates.Count == 0)
            {
                return Failure(messageId, "No se encontró ningún fichero adjunto en el mensaje (se esperan attachments contentType='reference' con contentUrl; las imágenes/vídeos van en el body y tienen otras herramientas).",
                    message, [], attempts, authenticatedUserId, index);
            }

            if (index < 0 || index >= candidates.Count)
            {
                return Failure(messageId,
                    $"Índice {index} fuera de rango: el mensaje tiene {candidates.Count} fichero(s) adjunto(s) (0..{candidates.Count - 1}).",
                    message, candidates, attempts, authenticatedUserId, index);
            }

            var candidate = candidates[index];
            var cacheKey = $"{chatId}|{messageId}|{candidate.Url ?? candidate.Name}";
            var cached = Cache.TryGet(cacheKey, out var file);

            if (!cached)
            {
                byte[] bytes;
                try
                {
                    bytes = await TeamsAttachmentTools.DownloadAsync(client, chatId, messageId, candidate.Url, null);
                }
                catch (Exception ex)
                {
                    attempts.Add(TeamsAttachmentTools.Attempt($"download ({candidate.Source})", candidate.Url, ex));
                    return Failure(messageId, $"No se pudo descargar el fichero ({candidate.Source}). " +
                        "Si el error es 403 accessDenied: el token necesita Files.Read.All (y normalmente Sites.Read.All) para leer el contentUrl de OneDrive, " +
                        "o usa authenticatedUserId con auth delegada. Los role app-only del tenant no incluyen esos permisos.",
                        message, candidates, attempts, authenticatedUserId, index);
                }

                if (bytes.Length == 0)
                {
                    return Failure(messageId, "Graph devolvió el fichero vacío.",
                        message, candidates, attempts, authenticatedUserId, index);
                }

                file = new CachedFile(SniffMime(bytes, candidate.Name), bytes);
                if (bytes.Length <= MaxFileBytes) Cache.Store(cacheKey, file);
            }

            return await BuildFileBlocksAsync(message, candidate, candidates.Count, index, file!, cached, attempts);
        });
    }

    [McpServerTool(Name = "teams_list_file_candidates")]
    [Description("Lista los ficheros adjuntos a un mensaje de Teams con sus metadatos (nombre, tipo, fuente, índice) sin descargar nada. Sirve para saber cuántos ficheros hay y qué índice usar en teams_get_file_content antes de leer, especialmente en mensajes con varios adjuntos.")]
    public static async Task<IList<ContentBlock>> ListFileCandidates(
        GraphService graph,
        ILogger<TeamsFileTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("ID del mensaje cuyos adjuntos listar.")] string messageId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithContentLogging<IList<ContentBlock>>("teams_list_file_candidates", authenticatedUserId, async () =>
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
                    null, [], attempts, authenticatedUserId, 0);
            }

            if (message is null)
            {
                return Failure(messageId, "Graph no devolvió ese mensaje (posiblemente no existe en ese chat).",
                    null, [], attempts, authenticatedUserId, 0);
            }

            var candidates = FindFileCandidates(message.Attachments);
            var from = message.From?.User?.DisplayName ?? "desconocido";
            var created = message.CreatedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "sin fecha";

            var text = new StringBuilder();
            text.Append("El mensaje ").Append(message.Id);
            if (candidates.Count == 0)
            {
                text.Append(" de ").Append(from).Append(" (").Append(created)
                    .Append(") no tiene ficheros legibles adjuntos (se esperan attachments contentType='reference' con contentUrl; " +
                    "las imágenes/vídeos van en el body y tienen otras herramientas).");
            }
            else
            {
                text.Append(" de ").Append(from).Append(" (").Append(created)
                    .Append(") tiene ").Append(candidates.Count).Append(" fichero(s):");
                for (var i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    text.Append("\n[").Append(i).Append("] ").Append(c.Name ?? "(sin nombre)")
                        .Append(" — ").Append(c.ContentType)
                        .Append(" (").Append(c.Source).Append(')');
                    if (!string.IsNullOrEmpty(c.Url)) text.Append("\n    url: ").Append(c.Url);
                }
                text.Append("\n\nUsa teams_get_file_content con chatId, messageId y el índice elegido para leer el contenido.");
            }

            return [new TextContentBlock { Text = text.ToString() }];
        });
    }

    /// <summary>Extrae el texto según el tipo y monta los content blocks. Todo es degradable.</summary>
    private static async Task<IList<ContentBlock>> BuildFileBlocksAsync(
        ChatMessage? message,
        FileCandidate candidate,
        int total,
        int index,
        CachedFile file,
        bool cached,
        List<object> attempts)
    {
        var ext = ExtensionOf(candidate.Name);
        var isTextLike = ext is ".txt" or ".csv" or ".md" or ".json" or ".log";

        if (file.Bytes.Length > MaxFileBytes)
        {
            return Failure(message?.Id ?? "?", $"Fichero demasiado grande ({file.Bytes.Length:N0} bytes > {MaxFileBytes:N0}).",
                message, [candidate], attempts, null, index);
        }

        var body = new StringBuilder();
        var pageImages = new List<byte[]>();

        try
        {
            if (ext == ".pdf" || PdfPageExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                var pdf = ExtractPdf(file.Bytes);
                if (pdf.HasExtractableText)
                {
                    body.Append(JoinPdf(pdf));
                }
                else
                {
                    // Escaneado: degradar a imágenes por página (pdftoppm de poppler).
                    pageImages = await RenderPdfPagesAsync(file.Bytes);
                    if (pageImages.Count == 0)
                    {
                        body.Append("PDF sin texto extraíble (escaneado o solo imagen) y no se pudo renderizar " +
                            "páginas: instala poppler (`brew install poppler`) y reintenta, o sube una versión con texto.");
                    }
                    else
                    {
                        body.Append($"{pageImages.Count} páginas adjuntas como imágenes (PDF escaneado sin texto extraíble).");
                    }
                }
            }
            else if (isTextLike)
            {
                var content = ReadPlainText(file.Bytes);
                body.Append(string.IsNullOrWhiteSpace(content) ? "El fichero de texto está vacío." : content);
            }
            else if (ext == ".docx") body.Append(ExtractDocxText(file.Bytes));
            else if (ext == ".xlsx") body.Append(ExtractXlsxText(file.Bytes));
            else if (ext == ".pptx") body.Append(ExtractPptxText(file.Bytes));
            else
            {
                return Failure(message?.Id ?? "?",
                    $"Tipo de fichero no soportado para extraer texto: extensión '{ext}' de '{candidate.Name}'. " +
                    "Soportados: pdf, docx, xlsx, pptx, txt, csv, md, json, log.",
                    message, [candidate], attempts, null, index);
            }
        }
        catch (Exception ex)
        {
            attempts.Add(TeamsAttachmentTools.Attempt($"parse ({ext})", candidate.Name, ex));
            return Failure(message?.Id ?? "?",
                $"No se pudo extraer el texto del fichero ({ext}): {ex.Message}",
                message, [candidate], attempts, null, index);
        }

        var from = message?.From?.User?.DisplayName ?? "desconocido";
        var created = message?.CreatedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "sin fecha";
        var contentString = body.ToString();
        var truncated = contentString.Length > MaxOutputChars;
        if (truncated) contentString = TruncateUtf16(contentString, MaxOutputChars);

        var text = new StringBuilder();
        text.Append("Fichero ").Append(index + 1).Append('/').Append(total)
            .Append(" del mensaje ").Append(message?.Id)
            .Append(" de ").Append(from)
            .Append(" (").Append(created).Append("). ")
            .Append(candidate.Name).Append(", ").Append(file.MimeType).Append(", ")
            .Append(file.Bytes.Length).Append(" bytes. ")
            .Append("Origen: ").Append(candidate.Source).Append(". ")
            .Append(cached ? "Bytes servidos de caché (no se ha vuelto a descargar). " : "");
        if (truncated) text.Append($"\n(Contenido cortado a {MaxOutputChars:N0} caracteres.)");
        if (contentString.Length > 0) text.Append("\n\n--- CONTENIDO ---\n").Append(contentString);
        text.Append("\n\nPrompt sugerido: ").Append(SuggestedPrompt);

        var blocks = new List<ContentBlock> { new TextContentBlock { Text = text.ToString() } };
        foreach (var bytes in pageImages)
        {
            blocks.Add(new ImageContentBlock
            {
                MimeType = "image/jpeg",
                Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(bytes))
            });
        }
        return blocks;
    }

    // ── Candidatos ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los ficheros de Teams llegan como attachments contentType='reference' con contentUrl de
    /// OneDrive. Se descartan: sin contentUrl, citas (messageReference), tarjetas adaptativas y
    /// medios (imagen/vídeo/audio), que son de otras herramientas. Un mensaje puede llevar varios
    /// ficheros (p. ej. "xlsx, pdf" en el real). Dedupe por URL.
    /// </summary>
    public static List<FileCandidate> FindFileCandidates(IEnumerable<ChatMessageAttachment>? attachments)
    {
        var result = new List<FileCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in attachments ?? [])
        {
            if (string.IsNullOrEmpty(a.ContentUrl)) continue;

            var ct = a.ContentType ?? "";
            if (ct.Contains("messageReference", StringComparison.OrdinalIgnoreCase)) continue;
            if (ct.Contains("adaptive", StringComparison.OrdinalIgnoreCase)) continue;
            if (ct.Contains("image/", StringComparison.OrdinalIgnoreCase)) continue;
            if (ct.Contains("audio/", StringComparison.OrdinalIgnoreCase)) continue;
            if (ct.Contains("video/", StringComparison.OrdinalIgnoreCase)) continue;

            var name = a.Name;
            if (!string.IsNullOrEmpty(name) && MediaExtensions.Contains(ExtensionOf(name), StringComparer.OrdinalIgnoreCase)) continue;

            var source = ct.StartsWith("reference", StringComparison.OrdinalIgnoreCase)
                ? "attachment.reference"
                : "attachment.contentUrl";
            if (!seen.Add(a.ContentUrl)) continue;
            result.Add(new FileCandidate(source, a.ContentUrl, name, ct));
        }

        return result;
    }

    // ── Extracción ──────────────────────────────────────────────────────────────────────────────

    /// <summary>PDF digital con PdfPig: una cadena por página. HasExtractableText=false si es escaneado.</summary>
    public static PdfExtraction ExtractPdf(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = PdfDocument.Open(ms);
        var pages = new List<string>();
        long totalChars = 0;

        foreach (var page in doc.GetPages())
        {
            var pageText = page.Text?.Trim() ?? "";
            if (pages.Count >= MaxPdfPages) break;
            pages.Add(pageText);
            totalChars += pageText.Length;
        }

        return new PdfExtraction(pages, totalChars >= MinPdfTextChars);
    }

    private static string JoinPdf(PdfExtraction pdf)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pdf.Pages.Count; i++)
        {
            sb.Append("\n--- Página ").Append(i + 1).Append('/').Append(pdf.Pages.Count).Append(" ---\n");
            sb.Append(string.IsNullOrWhiteSpace(pdf.Pages[i]) ? "(sin texto)" : pdf.Pages[i]);
        }
        return sb.ToString();
    }

/// <summary>PDF escaneado → páginas como JPEG con pdftoppm (poppler). Devuelve los bytes en orden; borra los temporales en finally.</summary>
public static async Task<List<byte[]>> RenderPdfPagesAsync(byte[] bytes)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "zeroteams-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "doc.pdf");
            await File.WriteAllBytesAsync(input, bytes);
            var prefix = Path.Combine(tempDir, "page");
            await RunAsync(PdfToPpmArgs(input, prefix, MaxPdfPages, 100));
            return Directory.GetFiles(tempDir, "page-*.jpg")
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('-')[^1]))
                .Take(MaxPdfPages)
                .Select(f => File.ReadAllBytes(f))
                .ToList();
        }
        catch (Win32Exception) { return []; }  // poppler no instalado → la llamada degrada a solo diagnóstico
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* mejor esfuerzo */ }
        }
    }

    /// <summary>DOCX: párrafos (incluye los de tablas) en orden de documento.</summary>
    public static string ExtractDocxText(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "(documento vacío)";

        var sb = new StringBuilder();
        foreach (var p in body.Descendants<Paragraph>())
        {
            var t = p.InnerText?.Trim();
            if (!string.IsNullOrEmpty(t)) sb.AppendLine(t);
        }
        return sb.Length == 0 ? "(documento sin texto)" : sb.ToString().TrimEnd();
    }

    /// <summary>XLSX: una sección por hoja con las celdas como filas "col:valor". El límite de celdas corta el resto de hojas con aviso.</summary>
    public static string ExtractXlsxText(byte[] bytes, int maxRows = MaxXlsxRows, int maxCells = MaxXlsxCells)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null) return "(libro sin workbook)";

        var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();
        var rowCount = 0;
        var cellCount = 0;
        var cellLimitHit = false;

        foreach (var sheet in workbookPart.Workbook.Sheets!.OfType<Sheet>())
        {
            if (cellLimitHit) break;
            sb.Append("\n--- Hoja: ").Append(sheet.Name).Append(" ---");
            if (sheet.Id?.Value is null) continue;
            var wsPart = workbookPart.GetPartById(sheet.Id!.Value!) as WorksheetPart;
            var rows = wsPart?.Worksheet?.Descendants<Row>().ToList() ?? [];

            if (rows.Count == 0) { sb.Append("\n(hoja vacía)"); continue; }
            foreach (var row in rows)
            {
                if (cellLimitHit) break;
                if (rowCount >= maxRows) { sb.Append($"\n… {rows.Count - rowCount} filas más sin mostrar (límite {maxRows})."); break; }
                rowCount++;

                var vals = new List<string>();
                foreach (var c in row.Descendants<Cell>())
                {
                    if (cellCount >= maxCells) { cellLimitHit = true; break; }
                    var raw = c.CellValue?.Text;
                    if (string.IsNullOrEmpty(raw)) continue;
                    cellCount++;

                    if (c.DataType is not null && c.DataType!.Value == CellValues.SharedString && shared is not null)
                    {
                        if (int.TryParse(raw, out var idx) && idx >= 0 && idx < shared.ChildElements.Count)
                            vals.Add(shared.ChildElements[idx].InnerText);
                    }
                    else
                    {
                        vals.Add(raw);
                    }
                }
                if (vals.Count > 0) sb.Append('\n').Append(string.Join(" | ", vals));
            }
            if (cellLimitHit) sb.Append($"\n… (límite de {maxCells:N0} celdas alcanzado, resto del libro sin mostrar).");
        }
        return sb.Length == 0 ? "(libro sin texto)" : sb.ToString().TrimEnd();
    }

    /// <summary>PPTX: texto por diapositiva en orden.</summary>
    public static string ExtractPptxText(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = PresentationDocument.Open(ms, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null) return "(presentación sin slide part)";

        var sb = new StringBuilder();
        var slideNum = 0;
        foreach (var slidePart in presentationPart.SlideParts)
        {
            slideNum++;
            var texts = slidePart.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                .Select(t => t.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList() ?? [];

            sb.Append("\n--- Diapositiva ").Append(slideNum).Append(" ---");
            if (texts.Count == 0) { sb.Append("\n(sin texto)"); continue; }
            foreach (var t in texts) sb.Append('\n').Append(t);
        }
        return sb.Length == 0 ? "(presentación sin texto)" : sb.ToString().TrimEnd();
    }

    public static string ReadPlainText(byte[] bytes)
    {
        // Los CSV de Teams vienen a veces con BOM UTF-8; Encoding.UTF8 lo respeta.
        return Encoding.UTF8.GetString(bytes).Trim();
    }

    /// <summary>
    /// Corta a los <paramref name="maxChars"/> primeros caracteres UTF-16 sin partir un par de
    /// surrogates: si el corte cae entre high y low surrogate, retrocede un carácter.
    /// </summary>
    public static string TruncateUtf16(string value, int maxChars)
    {
        if (maxChars < 0) maxChars = 0;
        if (value.Length <= maxChars) return value;

        var end = maxChars;
        if (end > 0 && end < value.Length &&
            char.IsHighSurrogate(value[end - 1]) && char.IsLowSurrogate(value[end]))
        {
            end--;
        }
        return value[..end];
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────────────────────

    public static string ExtensionOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var ext = Path.GetExtension(TeamsAttachmentTools.StripQuery(name)) ?? "";
        return ext.ToLowerInvariant();
    }

    public static string SniffMime(byte[] b, string? name)
    {
        if (b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F') return "application/pdf";
        if (b.Length > 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) return "application/zip"; // docx/xlsx/pptx
        if (b.Length > 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return "text/plain"; // BOM UTF-8
        var ext = ExtensionOf(name);
        switch (ext)
        {
            case ".pdf": return "application/pdf";
            case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            case ".pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
            case ".json": return "application/json";
            case ".csv": return "text/csv";
            case ".md": return "text/markdown";
            case ".log": return "text/plain";
            default: return "application/octet-stream";
        }
    }

    public static string[] PdfToPpmArgs(string input, string outputPrefix, int maxPages, int dpi) =>
    [
        "pdftoppm", "-jpeg", "-r", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-f", "1", "-l", maxPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
        input, outputPrefix
    ];

    /// <summary>Ejecuta un proceso con ArgumentList (sin shell). Timeout 120s. Lanza con stderr si sale != 0.</summary>
    private static async Task<string> RunAsync(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = args[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args[1..]) psi.ArgumentList.Add(a);

        using var p = new System.Diagnostics.Process { StartInfo = psi };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(ProcessTimeoutMs);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ya murió */ }
            throw new TimeoutException($"'{args[0]}' superó {ProcessTimeoutMs / 1000} s.");
        }

        if (p.ExitCode != 0)
        {
            var err = await stderr;
            throw new InvalidOperationException($"{args[0]} exit {p.ExitCode}: {err.Trim()[..Math.Min(err.Trim().Length, 500)]}");
        }
        return await stdout;
    }

    // ── Errores ─────────────────────────────────────────────────────────────────────────────────

    private static List<ContentBlock> Failure(
        string messageId,
        string reason,
        ChatMessage? message,
        List<FileCandidate> candidates,
        List<object> attempts,
        string? authenticatedUserId,
        int index)
    {
        var payload = new
        {
            MessageId = messageId,
            Index = index,
            Reason = reason,
            Candidates = candidates,
            Attempts = attempts,
            Diagnostics = new
            {
                RequestedAuth = authenticatedUserId is null ? "app-only" : "delegated (GraphService cae a app-only si falla el token delegado)",
                Attachments = message?.Attachments?.Select(a => new { a.Id, a.ContentType, a.ContentUrl, a.Name })
            }
        };

        return [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOpts) }];
    }
}