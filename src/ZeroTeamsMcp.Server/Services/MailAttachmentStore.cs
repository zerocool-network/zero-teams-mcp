using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Guarda el adjunto en disco. La respuesta del tool no lleva los bytes:
/// el modelo solo recibe la ruta y, si hay HTTP, una URL corta.
/// </summary>
public static class MailAttachmentStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    public sealed record Saved(string Id, string Name, string ContentType, long Size, string Path, string? Url);

    private sealed record Meta(string Name, string ContentType, long Size);

    public static string Root()
    {
        var configured = Environment.GetEnvironmentVariable("MAIL_ATTACHMENT_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "downloads"));
    }

    public static Saved Save(string? name, string? contentType, byte[] bytes, string? url = null, string? root = null)
    {
        var directory = root ?? Root();
        Directory.CreateDirectory(directory);
        Sweep(directory);

        var id = Guid.NewGuid().ToString("n");
        var safe = SafeName(name);
        var folder = Path.Combine(directory, id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, safe);
        File.WriteAllBytes(path, bytes);
        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var meta = new Meta(safe, type, bytes.Length);
        File.WriteAllText(Path.Combine(folder, "meta.json"), JsonSerializer.Serialize(meta, JsonOpts));
        return new Saved(id, safe, type, bytes.Length, path, url);
    }

    public static (string Path, string Name, string ContentType)? Open(string? id, string? root = null)
    {
        if (!IsId(id)) return null;
        var directory = Path.GetFullPath(root ?? Root());
        var folder = Path.GetFullPath(Path.Combine(directory, id!));
        if (!folder.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;
        var metaPath = Path.Combine(folder, "meta.json");
        if (!File.Exists(metaPath)) return null;
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath), JsonOpts);
        if (meta is null || string.IsNullOrWhiteSpace(meta.Name)) return null;
        var file = Path.GetFullPath(Path.Combine(folder, meta.Name));
        if (!file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;
        if (!File.Exists(file)) return null;
        return (file, meta.Name, meta.ContentType);
    }

    public static string? UrlFor(HttpRequest? request, string id)
    {
        if (request is null) return null;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
            host = request.Host.Value;
        if (string.IsNullOrWhiteSpace(host)) return null;
        var proto = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(proto))
            proto = request.Scheme;
        return proto + "://" + host + "/mcp/files/" + id;
    }

    public static string SafeName(string? name)
    {
        var file = Path.GetFileName(name ?? "");
        var cleaned = new string(file.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch).ToArray()).Trim();
        if (cleaned is "" or "." or "..")
            return "adjunto";
        return cleaned;
    }

    private static bool IsId(string? id) =>
        id is { Length: 32 } && id.All(Uri.IsHexDigit);

    private static void Sweep(string directory)
    {
        var limit = DateTime.UtcNow - KeepFor;
        foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(folder) < limit)
                    Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
