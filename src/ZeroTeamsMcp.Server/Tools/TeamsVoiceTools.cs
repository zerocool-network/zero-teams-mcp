using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class TeamsVoiceTools
{
    // ponytail: cache en memoria del proceso; se pierde al reiniciar. Si hace falta persistir, mover a Redis (ya lo usa ControlPlane).
    private static readonly ConcurrentDictionary<string, VoiceTranscript> Cache = new();
    private static readonly HttpClient Stt = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static readonly string[] AudioExtensions = [".mp4", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".webm", ".mp3", ".amr"];

    public sealed record VoiceTranscript(string MessageId, string Transcript, double? DurationSeconds, double? Confidence);

    public sealed record AudioCandidate(string Source, string? Url, string? HostedContentId, string? ContentType, string? Name);

    [McpServerTool(Name = "teams_get_voice_message_text")]
    [Description("Obtiene la transcripción de una nota de voz de un chat de Teams. Solo lectura. Requiere chatId y messageId. Si no se puede descargar/transcribir, devuelve diagnóstico con el shape real de attachments/hostedContents y los errores de Graph.")]
    public static async Task<string> GetVoiceMessageText(
        GraphService graph,
        ILogger<TeamsVoiceTools> logger,
        [Description("ID del chat.")] string chatId,
        [Description("ID del mensaje que contiene la nota de voz.")] string messageId,
        [Description("Opcional. ID del usuario autenticado para auth delegada.")] string? authenticatedUserId = null)
    {
        AuthenticatedUserIdValidator.Validate(authenticatedUserId);
        return await logger.ExecuteWithLogging<object>("teams_get_voice_message_text", authenticatedUserId, async () =>
        {
            var cacheKey = $"{chatId}|{messageId}";
            if (Cache.TryGetValue(cacheKey, out var hit))
            {
                return new { hit.MessageId, hit.Transcript, hit.DurationSeconds, hit.Confidence, Cached = true };
            }

            var client = await graph.GetClientAsync(authenticatedUserId);
            var attempts = new List<object>();

            var message = await client.Chats[chatId].Messages[messageId].GetAsync();

            List<ChatMessageHostedContent>? hosted = null;
            try
            {
                hosted = (await client.Chats[chatId].Messages[messageId].HostedContents.GetAsync())?.Value;
            }
            catch (Exception ex)
            {
                attempts.Add(Attempt("list hostedContents", null, ex));
            }

            var candidates = FindAudioCandidates(message?.Attachments, hosted, message?.Body?.Content);
            string? reason = null;

            foreach (var c in candidates)
            {
                byte[] audio;
                try
                {
                    audio = await DownloadAsync(client, chatId, messageId, c);
                }
                catch (Exception ex)
                {
                    attempts.Add(Attempt($"download ({c.Source})", c.Url ?? c.HostedContentId, ex));
                    continue;
                }

                try
                {
                    var result = await TranscribeAsync(messageId, audio, FileNameFor(c));
                    result = result with { DurationSeconds = CardDurationSeconds(message?.Attachments) ?? result.DurationSeconds };
                    Cache[cacheKey] = result;
                    return new { result.MessageId, result.Transcript, result.DurationSeconds, result.Confidence, Cached = false };
                }
                catch (Exception ex)
                {
                    reason = $"Audio descargado ({audio.Length} bytes vía {c.Source}) pero falló la transcripción: {ex.Message}";
                    break;
                }
            }

            reason ??= candidates.Count == 0
                ? "No se encontró ningún adjunto/hostedContent de audio en el mensaje."
                : "Se encontró audio pero no se pudo descargar.";

            return new
            {
                MessageId = messageId,
                Transcript = (string?)null,
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
        });
    }

    // La tarjeta de voz trae "duration" en ISO 8601 (p.ej. "PT2M5S").
    public static double? CardDurationSeconds(IEnumerable<ChatMessageAttachment>? attachments)
    {
        foreach (var a in attachments ?? [])
        {
            if (a.ContentType != "application/vnd.microsoft.card.audio" || string.IsNullOrWhiteSpace(a.Content)) continue;
            try
            {
                using var doc = JsonDocument.Parse(a.Content);
                if (doc.RootElement.TryGetProperty("duration", out var d) && d.GetString() is { } iso)
                    return System.Xml.XmlConvert.ToTimeSpan(iso).TotalSeconds;
            }
            catch (Exception e) when (e is JsonException or FormatException) { }
        }
        return null;
    }

    // Shape verificado (2026-09-24, tenant Optima): contentType "application/vnd.microsoft.card.audio",
    // content = {"duration":"PT2M5S","media":[{"url":".../hostedContents/{id}/$value"}]}; hostedContent.contentType = null.
    // Resto de ramas: heurística tolerante para variantes no vistas.
    public static List<AudioCandidate> FindAudioCandidates(
        IEnumerable<ChatMessageAttachment>? attachments,
        IEnumerable<ChatMessageHostedContent>? hosted,
        string? bodyHtml)
    {
        var result = new List<AudioCandidate>();

        foreach (var a in attachments ?? [])
        {
            var ct = a.ContentType ?? "";
            var looksAudio = ct.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                || ct.Contains("voice", StringComparison.OrdinalIgnoreCase)
                || ct.Contains("audio", StringComparison.OrdinalIgnoreCase)
                || HasAudioExtension(a.ContentUrl) || HasAudioExtension(a.Name);

            if (!looksAudio) continue;

            if (!string.IsNullOrEmpty(a.ContentUrl))
            {
                result.Add(new AudioCandidate("attachment.contentUrl", a.ContentUrl, null, ct, a.Name));
            }

            // Tarjetas (card.audio / card.voice) suelen llevar la URL dentro del JSON de content.
            foreach (var url in UrlsInJson(a.Content))
            {
                result.Add(new AudioCandidate("attachment.content", url, null, ct, a.Name));
            }
        }

        foreach (var h in hosted ?? [])
        {
            if (h.Id is not null && (h.ContentType ?? "").StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new AudioCandidate("hostedContent", null, h.Id, h.ContentType, null));
            }
        }

        // Fallback: si el mensaje es solo un <attachment> sin imagen, probar hostedContents cuyo tipo no conocemos.
        var bodyIsBareAttachment = bodyHtml?.Contains("<attachment", StringComparison.OrdinalIgnoreCase) == true
            && !bodyHtml.Contains("<img", StringComparison.OrdinalIgnoreCase);
        if (result.Count == 0 && bodyIsBareAttachment)
        {
            foreach (var h in hosted ?? [])
            {
                if (h.Id is not null && !(h.ContentType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new AudioCandidate("hostedContent (tipo desconocido)", null, h.Id, h.ContentType, null));
                }
            }
        }

        return result;
    }

    private static Task<byte[]> DownloadAsync(GraphServiceClient client, string chatId, string messageId, AudioCandidate c)
        => TeamsAttachmentTools.DownloadAsync(client, chatId, messageId, c.Url, c.HostedContentId);

    /// <summary>STT ElevenLabs Scribe. Público para reutilizarlo desde TeamsVideoTools (vídeo → frames + transcripción).</summary>
    public static async Task<VoiceTranscript> TranscribeAsync(string messageId, byte[] audio, string fileName)
    {
        var apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Audio descargado pero ELEVENLABS_API_KEY no está configurada para transcribir.");
        }

        var model = Environment.GetEnvironmentVariable("ELEVENLABS_STT_MODEL") ?? "scribe_v1";

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(model), "model_id");
        form.Add(new StringContent("false"), "tag_audio_events");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.elevenlabs.io/v1/speech-to-text") { Content = form };
        req.Headers.Add("xi-api-key", apiKey);

        using var resp = await Stt.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ElevenLabs STT {(int)resp.StatusCode}: {body}");
        }

        return ParseScribe(messageId, body);
    }

    // Respuesta Scribe: { text, language_probability, words: [{ text, start, end, type, logprob? }] }
    public static VoiceTranscript ParseScribe(string messageId, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = root.GetProperty("text").GetString() ?? "";

        double? duration = null;
        var logprobs = new List<double>();
        if (root.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in words.EnumerateArray())
            {
                if (w.TryGetProperty("end", out var end) && end.ValueKind == JsonValueKind.Number)
                    duration = Math.Max(duration ?? 0, end.GetDouble());
                if (w.TryGetProperty("type", out var t) && t.GetString() == "word"
                    && w.TryGetProperty("logprob", out var lp) && lp.ValueKind == JsonValueKind.Number)
                    logprobs.Add(lp.GetDouble());
            }
        }

        // Confianza = media de exp(logprob) por palabra; null si Scribe no manda logprobs.
        double? confidence = logprobs.Count > 0 ? Math.Round(logprobs.Average(Math.Exp), 3) : null;
        return new VoiceTranscript(messageId, text.Trim(), duration, confidence);
    }

    private static object Attempt(string step, string? target, Exception ex) => TeamsAttachmentTools.Attempt(step, target, ex);

    private static bool HasAudioExtension(string? s) =>
        !string.IsNullOrEmpty(s) && AudioExtensions.Any(e => StripQuery(s).EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string StripQuery(string s) => TeamsAttachmentTools.StripQuery(s);

    private static IEnumerable<string> UrlsInJson(string? json) => TeamsAttachmentTools.UrlsInJson(json);

    private static string FileNameFor(AudioCandidate c)
    {
        var fromName = c.Name ?? (c.Url is null ? null : Path.GetFileName(StripQuery(c.Url)));
        if (HasAudioExtension(fromName)) return fromName!;
        return (c.ContentType ?? "").ToLowerInvariant() switch
        {
            var t when t.Contains("ogg") => "audio.ogg",
            var t when t.Contains("wav") => "audio.wav",
            var t when t.Contains("webm") => "audio.webm",
            var t when t.Contains("mpeg") || t.Contains("mp3") => "audio.mp3",
            _ => "audio.mp4" // AAC en contenedor MP4 es lo más probable para Teams.
        };
    }
}
