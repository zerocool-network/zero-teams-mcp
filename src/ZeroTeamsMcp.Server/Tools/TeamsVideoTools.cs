using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Lectura de vídeos de un chat de Teams SIN modelos de vídeo externos: la herramienta descarga el
/// mp4 de Graph, ffmpeg extrae (a) los frames clave como imágenes MCP — que lee el modelo cliente
/// multimodal — y (b) la pista de audio, que se transcribe con ElevenLabs Scribe (mismo STT que
/// teams_get_voice_message_text). Si no hay audio o falla la transcripción, degrada a solo frames.
///
/// Shape verificado 2026-10-06 (tenant Optima, chat 19:9b04bb1…@thread.v2, msgs 1791278477365 /
/// 1791278337771 de Fran Ullod): attachments = []; el vídeo solo aparece en body.content como
/// &lt;video src="…/hostedContents/{base64Id}/$value" data-duration="PT1M0S" style="width:540px;
/// height:960px"&gt;. El $value devuelve Content-Type video/mp4 (mp4 h264+aac, 60s, 9.5MB en el real).
/// Requiere ffmpeg/ffprobe en el PATH (homebrew).
/// </summary>
[McpServerToolType]
public sealed class TeamsVideoTools
{
    // Bytes ya descargados; se pierde al reiniciar el proceso.
    private static readonly ConcurrentDictionary<string, CachedVideo> Cache = new();

    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".webm", ".m4v", ".avi", ".mkv"];

    // Vídeos de Teams son capturas de pantalla cortas (el real: 9.5MB/60s). Tope anti-blob gigante.
    private const long MaxVideoBytes = 60 * 1024 * 1024;

    private const int MaxFrames = 6;
    private const double SceneThreshold = 0.3;
    private const int ProcessTimeoutMs = 120_000;

    private const string SuggestedPrompt =
        "Describe en español lo que ocurre en esta secuencia de frames de un vídeo (probablemente una grabación de pantalla de una aplicación): " +
        "qué interfaz se ve, qué acciones o cambios se aprecian entre frames y transcribe el texto relevante. " +
        "Si los frames no bastan para entenderlo, dilo en vez de inventar.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public sealed record CachedVideo(string MimeType, byte[] Bytes);

    public sealed record VideoCandidate(string Source, string? Url, string? HostedContentId, string? ContentType, string? Name);

    public sealed record VideoProbe(double? DurationSeconds, int? Width, int? Height, bool HasAudio);

    [McpServerTool(Name = "teams_get_video_description")]
    [Description("Descarga el vídeo de un mensaje de Teams (grabación de pantalla, clip) y lo convierte en material legible: transcripción del audio (si lo tiene) + hasta 6 frames clave como imágenes MCP para que tú (modelo cliente) lo leas. No usa ningún modelo de vídeo externo; requiere ffmpeg instalado. Devuelve un bloque de texto con metadatos y transcripción y varios bloques de imagen. Si falla, bloque de texto con diagnóstico JSON.")]
    public static async Task<IList<ContentBlock>> GetVideoDescription(
        GraphService graph,
        ILogger<TeamsVideoTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("ID del mensaje que contiene el vídeo.")] string messageId,
        [Description("Opcional. Índice del vídeo si el mensaje tiene varios (0-based). Default: 0.")] int index = 0,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithContentLogging<IList<ContentBlock>>("teams_get_video_description", authenticatedUserId, async () =>
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

            var candidates = FindVideoCandidates(message?.Attachments, hosted, message?.Body?.Content);
            if (candidates.Count == 0)
            {
                return Failure(messageId, "No se encontró ningún vídeo en el mensaje (ni en attachments, ni como <video> en el body).",
                    message, hosted, candidates, attempts, authenticatedUserId, index);
            }

            if (index < 0 || index >= candidates.Count)
            {
                return Failure(messageId,
                    $"Índice {index} fuera de rango: el mensaje tiene {candidates.Count} vídeo(s) (0..{candidates.Count - 1}).",
                    message, hosted, candidates, attempts, authenticatedUserId, index);
            }

            var candidate = candidates[index];
            var cacheKey = $"{chatId}|{messageId}|{candidate.Url ?? candidate.HostedContentId}";
            var cached = Cache.TryGetValue(cacheKey, out var video);

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
                    return Failure(messageId, $"No se pudo descargar el vídeo ({candidate.Source}).",
                        message, hosted, candidates, attempts, authenticatedUserId, index);
                }

                if (bytes.Length == 0)
                {
                    return Failure(messageId, "Graph devolvió el vídeo vacío.",
                        message, hosted, candidates, attempts, authenticatedUserId, index);
                }

                video = new CachedVideo(SniffVideoMime(bytes), bytes);
                if (bytes.Length <= MaxVideoBytes) Cache[cacheKey] = video;
            }

            return await BuildVideoBlocksAsync(logger, message, candidate, candidates.Count, index, video!, cached, attempts);
        });
    }

    /// <summary>
    /// ffmpeg/ffprobe sobre los bytes en temporal: metadatos, frames clave (escena, fallback
    /// equidistante) y pista de audio → transcripción ElevenLabs. Todo es degradable: sin ffmpeg se
    /// devuelve diagnóstico; sin audio o sin STT, solo frames; sin frames, solo texto.
    /// </summary>
    private static async Task<IList<ContentBlock>> BuildVideoBlocksAsync(
        ILogger logger,
        ChatMessage? message,
        VideoCandidate candidate,
        int total,
        int index,
        CachedVideo video,
        bool cached,
        List<object> attempts)
    {
        var bodyHtml = message?.Body?.Content;

        var tempDir = Path.Combine(Path.GetTempPath(), "zeroteams-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "video" + ExtensionFor(video.MimeType));
            await File.WriteAllBytesAsync(input, video.Bytes);

            // 1) Metadatos (ffprobe) con fallback al body (data-duration / style width/height).
            VideoProbe? probe = null;
            try
            {
                var json = await RunAsync(FfprobeArgs(input));
                probe = ParseProbeJson(json);
            }
            catch (Win32Exception ex)
            {
                attempts.Add(Attempt("ffprobe", null, ex));
                return Failure(messageId: message?.Id ?? "?",
                    reason: "ffprobe/ffmpeg no están instalados (instálalos con `brew install ffmpeg` y reinicia el servidor MCP).",
                    message, null, [candidate], attempts, null, index);
            }
            catch (Exception ex)
            {
                attempts.Add(Attempt("ffprobe", null, ex));
            }

            var duration = probe?.DurationSeconds ?? BodyDurationSeconds(bodyHtml);
            var width = probe?.Width ?? BodyIntAttr(bodyHtml, "width");
            var height = probe?.Height ?? BodyIntAttr(bodyHtml, "height");

            // 2) Frames clave (escena; si la detección no encuentra cortes, muestreo equidistante).
            var frames = new List<string>();
            try
            {
                frames = await ExtractFramesAsync(input, tempDir, probe?.DurationSeconds);
            }
            catch (Win32Exception ex)
            {
                attempts.Add(Attempt("ffmpeg (frames)", null, ex));
                return Failure(message?.Id ?? "?",
                    "ffmpeg no está instalado (instálalo con `brew install ffmpeg` y reinicia el servidor MCP).",
                    message, null, [candidate], attempts, null, index);
            }
            catch (Exception ex)
            {
                attempts.Add(Attempt("ffmpeg (frames)", null, ex));
            }

            // 3) Audio → transcripción ElevenLabs (degradable).
            string? transcript = null;
            string? audioNote = null;
            if (probe is null || probe.HasAudio)
            {
                try
                {
                    var audio = Path.Combine(tempDir, "audio.m4a");
                    await RunAsync(AudioExtractArgs(input, audio));
                    var audioBytes = await File.ReadAllBytesAsync(audio);
                    var result = await TeamsVoiceTools.TranscribeAsync(message?.Id ?? "?", audioBytes, "video_audio.m4a");
                    transcript = string.IsNullOrWhiteSpace(result.Transcript) ? null : result.Transcript;
                    if (transcript is null) audioNote = "El audio se descargó pero la transcripción salió vacía.";
                }
                catch (Exception ex)
                {
                    attempts.Add(Attempt("audio → transcripción", null, ex));
                    audioNote = probe?.HasAudio == true
                        ? $"Hay pista de audio pero no se pudo transcribir: {ex.Message}"
                        : "Sin pista de audio transcribible.";
                }
            }
            else
            {
                audioNote = "El vídeo no tiene pista de audio.";
            }

            if (frames.Count == 0 && transcript is null && audioNote is null && probe is null)
            {
                return Failure(message?.Id ?? "?", "No se pudo extraer ni frames ni audio del vídeo (ffprobe falló sin diagnóstico claro).",
                    message, null, [candidate], attempts, null, index);
            }

            return BuildBlocks(message, candidate, total, index, video, cached, duration, width, height, frames, transcript, audioNote);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* temporal suelto no rompe nada */ }
        }
    }

    private static List<ContentBlock> BuildBlocks(
        ChatMessage? message,
        VideoCandidate candidate,
        int total,
        int index,
        CachedVideo video,
        bool cached,
        double? duration,
        int? width,
        int? height,
        List<string> frames,
        string? transcript,
        string? audioNote)
    {
        var from = message?.From?.User?.DisplayName ?? "desconocido";
        var created = message?.CreatedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "sin fecha";
        var dims = width is not null && height is not null ? $"{width}x{height}px" : "dimensiones desconocidas";
        var dur = duration is not null ? $"{Math.Round(duration.Value, 1)} s" : "duración desconocida";

        var sb = new StringBuilder();
        sb.Append("Vídeo ").Append(index + 1).Append('/').Append(total)
          .Append(" del mensaje ").Append(message?.Id)
          .Append(" de ").Append(from)
          .Append(" (").Append(created).Append("). ")
          .Append(video.MimeType).Append(", ").Append(video.Bytes.Length).Append(" bytes, ")
          .Append(dur).Append(", ").Append(dims).Append(". ")
          .Append("Origen: ").Append(candidate.Source).Append(". ")
          .Append(cached ? "Bytes servidos de caché (no se ha vuelto a descargar). " : "");
        sb.Append(frames.Count > 0
            ? $"\n\n{frames.Count} frames clave adjuntos (detectados por cambio de escena, máximo {MaxFrames}), en orden cronológico."
            : "\n\nNo se pudieron extraer frames del vídeo.");
        if (transcript is not null)
            sb.Append("\n\nTranscripción del audio:\n").Append(transcript);
        else if (audioNote is not null)
            sb.Append("\n\nAudio: ").Append(audioNote);
        sb.Append("\n\nPrompt sugerido: ").Append(SuggestedPrompt);

        var blocks = new List<ContentBlock> { new TextContentBlock { Text = sb.ToString() } };
        foreach (var frame in frames)
        {
            var bytes = File.ReadAllBytes(frame);
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
    /// Heurística tolerante para localizar los vídeos de un mensaje, espejo de FindImageCandidates:
    /// primero body HTML (&lt;video src&gt;), luego attachments video/* o extensión, y por último
    /// hostedContents con tipo video/* explícito. No hay fallback de hostedContents "desconocidos"
    /// como en imágenes: si el body trae el vídeo, su &lt;video src&gt; ya aporta la URL.
    /// </summary>
    public static List<VideoCandidate> FindVideoCandidates(
        IEnumerable<ChatMessageAttachment>? attachments,
        IEnumerable<ChatMessageHostedContent>? hosted,
        string? bodyHtml)
    {
        var result = new List<VideoCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string source, string? url, string? hostedId, string? contentType, string? name)
        {
            if (url is not null && !seen.Add(url)) return;
            if (url is null && hostedId is not null && !seen.Add(hostedId)) return;
            result.Add(new VideoCandidate(source, url, hostedId, contentType, name));
        }

        // 1) Fuente real en Teams: <video src> dentro del body HTML.
        foreach (var url in VideoSourcesInHtml(bodyHtml))
        {
            Add("body<video src>", TeamsAttachmentTools.StripQuery(url), null, null, null);
        }

        // 2) attachments con contentType video/card.video o extensión de vídeo.
        foreach (var a in attachments ?? [])
        {
            var ct = a.ContentType ?? "";
            var looksVideo = ct.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || ct.Contains("video", StringComparison.OrdinalIgnoreCase)
                || HasVideoExtension(a.ContentUrl) || HasVideoExtension(a.Name);

            if (!looksVideo) continue;

            if (!string.IsNullOrEmpty(a.ContentUrl)) Add("attachment.contentUrl", a.ContentUrl, null, ct, a.Name);

            foreach (var url in TeamsAttachmentTools.UrlsInJson(a.Content))
            {
                if (HasVideoExtension(url) || ct.Contains("video", StringComparison.OrdinalIgnoreCase))
                    Add("attachment.content", url, null, ct, a.Name);
            }
        }

        // 3) hostedContents con contentType video/* explícito (inofensivo si el tenant no lo usa).
        foreach (var h in hosted ?? [])
        {
            if (h.Id is not null && (h.ContentType ?? "").StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                Add("hostedContent", null, h.Id, h.ContentType, null);
            }
        }

        return result;
    }

    /// <summary>
    /// Extrae las URLs de todas las etiquetas &lt;video src&gt; del body HTML. Parseo propio (no
    /// reutiliza ImageTagsInHtml): el tag real lleva data-duration y style, y el &lt;source&gt;
    /// interno —que Teams no usa— se ignora.
    /// </summary>
    public static IEnumerable<string> VideoSourcesInHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) yield break;
        var i = 0;
        while (true)
        {
            var idx = html.IndexOf("<video", i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) yield break;
            var end = html.IndexOf('>', idx);
            if (end < 0) yield break;
            var tag = html[idx..(end + 1)];
            i = end + 1;

            var at = tag.IndexOf("src", StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                var after = at + 3;
                if (after < tag.Length && (char.IsLetterOrDigit(tag[after]) || tag[after] == '-'))
                {
                    at = tag.IndexOf("src", after, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                var eq = tag.IndexOf('=', after);
                if (eq < 0) break;
                var q = eq + 1 < tag.Length ? tag[eq + 1] : '\0';
                if (q is '"' or '\'')
                {
                    var close = tag.IndexOf(q, eq + 2);
                    if (close < 0) break;
                    var url = tag[(eq + 2)..close].Trim();
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        yield return url;
                    break;
                }
                at = tag.IndexOf("src", after, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ── Metadatos ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Salida de ffprobe -print_format json -show_format -show_streams; null si es basura o no trae ni streams ni format.</summary>
    public static VideoProbe? ParseProbeJson(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = doc.RootElement;

            var hasAudio = false;
            int? width = null, height = null;
            var hasStreams = root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array;
            if (hasStreams)
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                    if (type == "audio") hasAudio = true;
                    if (type == "video" && width is null)
                    {
                        if (s.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number) width = w.GetInt32();
                        if (s.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number) height = h.GetInt32();
                    }
                }
            }

            double? duration = null;
            if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object
                && format.TryGetProperty("duration", out var d)
                && (d.ValueKind == JsonValueKind.String || d.ValueKind == JsonValueKind.Number))
            {
                duration = d.ValueKind == JsonValueKind.Number
                    ? d.GetDouble()
                    : double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            }

            if (!hasStreams && duration is null) return null;
            return new VideoProbe(duration, width, height, hasAudio);
        }
    }

    /// <summary>Duración ISO-8601 del atributo data-duration del body (p. ej. "PT1M0S" → 60). Null si no parsea.</summary>
    public static double? ParseIsoDuration(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$");
        if (!m.Success) return null;
        var h = double.TryParse(m.Groups[1].Value, out var hv) ? hv : 0;
        var min = double.TryParse(m.Groups[2].Value, out var mv) ? mv : 0;
        var sec = double.TryParse(m.Groups[3].Value, out var sv) ? sv : 0;
        if (m.Groups[1].Success == false && m.Groups[2].Success == false && m.Groups[3].Success == false) return null;
        return h * 3600 + min * 60 + sec;
    }

    private static double? BodyDurationSeconds(string? bodyHtml)
    {
        if (string.IsNullOrEmpty(bodyHtml)) return null;
        var m = Regex.Match(bodyHtml, "data-duration=\"([^\"]+)\"");
        return m.Success ? ParseIsoDuration(m.Groups[1].Value) : null;
    }

    private static int? BodyIntAttr(string? bodyHtml, string name)
    {
        if (string.IsNullOrEmpty(bodyHtml)) return null;
        var m = Regex.Match(bodyHtml, $@"{name}:(\d+)px");
        if (m.Success) return int.TryParse(m.Groups[1].Value, out var v) ? v : null;
        m = Regex.Match(bodyHtml, $"{name}=\"(\\d+)\"");
        return m.Success && int.TryParse(m.Groups[1].Value, out var v2) ? v2 : null;
    }

    public static string SniffVideoMime(byte[] b)
    {
        if (b.Length > 11 && b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p') return "video/mp4";
        if (b.Length > 3 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return "video/webm";
        return "video/mp4";
    }

    private static string ExtensionFor(string mime) => mime switch
    {
        "video/webm" => ".webm",
        _ => ".mp4"
    };

    private static bool HasVideoExtension(string? s) =>
        !string.IsNullOrEmpty(s) && VideoExtensions.Any(e => TeamsAttachmentTools.StripQuery(s).EndsWith(e, StringComparison.OrdinalIgnoreCase));

    // ── ffmpeg ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Frames por cambio de escena (gt(scene,0.3)), escalados a ancho máx. 1280, sin audio.</summary>
    public static string[] SceneFrameArgs(string input, string outputPattern, int maxFrames) =>
    [
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-i", input,
        "-vf", $"select=gt(scene\\,{SceneThreshold.ToString(CultureInfo.InvariantCulture)}),scale=min(1280\\,iw):-2,format=yuvj420p",
        "-an", "-fps_mode", "vfr",
        "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture),
        "-q:v", "3",
        outputPattern
    ];

    /// <summary>Fallback sin cortes de escena: 1 frame cada <paramref name="intervalSeconds"/> segundos.</summary>
    public static string[] UniformFrameArgs(string input, string outputPattern, double intervalSeconds, int maxFrames)
    {
        if (!double.IsFinite(intervalSeconds) || intervalSeconds < 1) intervalSeconds = 10;
        var fps = "1/" + intervalSeconds.ToString(CultureInfo.InvariantCulture);
        return
        [
            "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
            "-i", input,
            "-vf", $"fps={fps},scale=min(1280\\,iw):-2,format=yuvj420p",
            "-an",
            "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture),
            "-q:v", "3",
            outputPattern
        ];
    }

    /// <summary>Pista de audio → m4a (aac re-codificado; el encoder aac siempre existe en ffmpeg).</summary>
    public static string[] AudioExtractArgs(string input, string output) =>
    [
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-i", input,
        "-map", "0:a:0", "-vn",
        "-c:a", "aac", "-b:a", "96k",
        output
    ];

    public static string[] FfprobeArgs(string input) =>
    [
        "ffprobe",
        "-v", "error",
        "-print_format", "json",
        "-show_format", "-show_streams",
        input
    ];

    private static async Task<List<string>> ExtractFramesAsync(string input, string tempDir, double? duration)
    {
        var scenePattern = Path.Combine(tempDir, "scene_%d.jpg");
        await RunAsync(SceneFrameArgs(input, scenePattern, MaxFrames));
        var frames = EnumerateFrames(tempDir, "scene_");
        if (frames.Count > 0) return frames;

        var interval = duration is > 0 ? Math.Ceiling(duration.Value / MaxFrames) : 10;
        var uniformPattern = Path.Combine(tempDir, "uniform_%d.jpg");
        await RunAsync(UniformFrameArgs(input, uniformPattern, interval, MaxFrames));
        return EnumerateFrames(tempDir, "uniform_");
    }

    private static List<string> EnumerateFrames(string dir, string prefix) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, prefix + "*.jpg")
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f)[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture))
                .ToList()
            : [];

    /// <summary>Ejecuta ffmpeg/ffprobe con ArgumentList (sin shell). Timeout 120s. Lanza con stderr si sale != 0.</summary>
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

    private static object Attempt(string step, string? target, Exception ex) => TeamsAttachmentTools.Attempt(step, target, ex);

    private static List<ContentBlock> Failure(
        string messageId,
        string reason,
        ChatMessage? message,
        List<ChatMessageHostedContent>? hosted,
        List<VideoCandidate> candidates,
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
}
