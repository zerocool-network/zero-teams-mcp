using System.Text;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;

namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Utilidades compartidas por las herramientas que descargan adjuntos de un mensaje de Teams
/// (audio e imagen). La descarga va autenticada contra Graph; nunca se manda el token a hosts
/// arbitrarios.
/// </summary>
internal static class TeamsAttachmentTools
{
    public static async Task<byte[]> DownloadAsync(GraphServiceClient client, string chatId, string messageId, string? url, string? hostedContentId)
    {
        Stream? stream;
        if (hostedContentId is not null)
        {
            stream = await client.Chats[chatId].Messages[messageId].HostedContents[hostedContentId].Content.GetAsync();
        }
        else
        {
            var uri = new Uri(url!);
            if (uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                var req = new RequestInformation { HttpMethod = Method.GET, URI = uri };
                stream = await client.RequestAdapter.SendPrimitiveAsync<Stream>(req);
            }
            else if (uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase))
            {
                // Requiere Files.Read.All / Sites.Read.All en el token.
                var shareId = "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url!)).TrimEnd('=').Replace('/', '_').Replace('+', '-');
                stream = await client.Shares[shareId].DriveItem.Content.GetAsync();
            }
            else
            {
                // No mandamos el token de Graph a hosts arbitrarios.
                throw new InvalidOperationException($"Host no soportado para descarga autenticada: {uri.Host}");
            }
        }

        if (stream is null) throw new InvalidOperationException("Graph devolvió cuerpo vacío.");
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public static object Attempt(string step, string? target, Exception ex) => new
    {
        Step = step,
        Target = target,
        Status = (ex as ApiException)?.ResponseStatusCode,
        Code = (ex as Microsoft.Graph.Models.ODataErrors.ODataError)?.Error?.Code,
        Error = (ex as Microsoft.Graph.Models.ODataErrors.ODataError)?.Error?.Message ?? ex.Message
    };

    public static string StripQuery(string s) => s.Split('?', '#')[0];

    public static IEnumerable<string> UrlsInJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { yield break; }
        using (doc)
        {
            var stack = new Stack<JsonElement>([doc.RootElement]);
            while (stack.Count > 0)
            {
                var el = stack.Pop();
                switch (el.ValueKind)
                {
                    case JsonValueKind.Object: foreach (var p in el.EnumerateObject()) stack.Push(p.Value); break;
                    case JsonValueKind.Array: foreach (var i in el.EnumerateArray()) stack.Push(i); break;
                    case JsonValueKind.String:
                        var v = el.GetString()!;
                        if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) yield return v;
                        break;
                }
            }
        }
    }

    public sealed record ImgTag(string Url, int? Width, int? Height);

    /// <summary>
    /// Extrae las URLs de todas las etiquetas &lt;img&gt; del body HTML del mensaje, con el ancho/alto
    /// declarados en la etiqueta. Es la fuente real de las imágenes en Teams: attachments viene vacío.
    /// </summary>
    public static IEnumerable<ImgTag> ImageTagsInHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) yield break;
        var i = 0;
        while (true)
        {
            var idx = html.IndexOf("<img", i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) yield break;
            var end = html.IndexOf('>', idx);
            if (end < 0) yield break;
            var tag = html[idx..(end + 1)];
            i = end + 1;

            var width = IntAttr(tag, "width");
            var height = IntAttr(tag, "height");

            foreach (var attr in new[] { "src", "srcset", "data-src" })
            {
                var at = tag.IndexOf(attr, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    var after = at + attr.Length;
                    // Evita matchear "srcset" cuando buscamos "src".
                    if (after < tag.Length && (char.IsLetterOrDigit(tag[after]) || tag[after] == '-'))
                    {
                        at = tag.IndexOf(attr, after, StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    var eq = tag.IndexOf('=', after);
                    if (eq < 0) break;
                    var q = eq + 1 < tag.Length ? tag[eq + 1] : '\0';
                    if (q is '"' or '\'')
                    {
                        var close = tag.IndexOf(q, eq + 2);
                        if (close < 0) break;
                        var val = tag[(eq + 2)..close].Trim();
                        // srcset: "url 1x, url 2x" → un candidato por descriptor.
                        foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            var url = part.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries)[0];
                            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                yield return new ImgTag(url, width, height);
                        }
                    }
                    at = tag.IndexOf(attr, after, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    public static IEnumerable<string> ImageSourcesInHtml(string? html) =>
        ImageTagsInHtml(html).Select(t => t.Url);

    /// <summary>Lee un atributo numérico de una etiqueta HTML (p. ej. width="357").</summary>
    public static int? IntAttr(string tag, string name)
    {
        var at = tag.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (at <= 0) return null;
        // Evita "data-width" al buscar "width".
        if (char.IsLetterOrDigit(tag[at - 1]) || tag[at - 1] == '-') return null;

        var start = at + name.Length + 1;
        if (start < tag.Length && (tag[start] == '"' || tag[start] == '\'')) start++;

        var end = start;
        while (end < tag.Length && char.IsDigit(tag[end])) end++;
        if (end == start) return null;

        return int.TryParse(tag[start..end], out var v) ? v : null;
    }
}