using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using ZeroTeamsMcp.Core.Graph;
using ZeroTeamsMcp.Server.Services;
using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

/// <summary>
/// Contrato de red de mail_search_hits: verifica en la request real a Graph
/// que el cursor se traduce a $skip, que $search nunca lleva $skip y que
/// coverage expone un cursor reanudable (ADR-0010, SDD §7.2.1).
/// </summary>
public sealed class MailSearchContractTests
{
    private const string Mailbox = "ana@example.com";

    private sealed class CapturingHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
        }
    }

    private static (GraphService Graph, CapturingHandler Handler) CreateGraph(
        Func<HttpRequestMessage, string> respond)
    {
        var handler = new CapturingHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(), httpClient: http);
        var client = new GraphServiceClient(adapter);
        return (GraphService.ForTest(client), handler);
    }

    private static ClientContext Context() => new(new HttpContextAccessor());

    private static string Query(Uri uri) => Uri.UnescapeDataString(uri.Query);

    private static string Page(string? nextLink) =>
        nextLink is null
            ? """{"value":[{"id":"m1","subject":"S","receivedDateTime":"2026-10-01T10:00:00Z"}]}"""
            : JsonSerializer.Serialize(new
            {
                value = new[] { new { id = "m1", subject = "S", receivedDateTime = "2026-10-01T10:00:00Z" } },
                odataNextLink = nextLink
            }).Replace("odataNextLink", "@odata.nextLink");

    [Fact]
    public async Task Cursor_Resume_RequestsSkip_And_ReturnsNextCursor()
    {
        const string next = "https://graph.microsoft.com/v1.0/users/ana@example.com/messages?$skip=51";
        var (graph, handler) = CreateGraph(_ => Page(next));
        var cursor = MailSearchTools.EncodeCursor(Mailbox, "sent", "2026-10-01", "2026-10-02", "", 50);

        var json = await MailSearchTools.SearchHits(
            graph, NullLogger<MailSearchTools>.Instance, Context(),
            userIds: "otro@example.com", folders: "inbox", ranges: "2026-01-01..2026-01-02",
            queries: "ignorada", cursor: cursor, maxCalls: 12);

        Assert.NotEmpty(handler.Requests);
        var first = Query(handler.Requests[0]);
        Assert.Contains("$skip=50", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$search", first, StringComparison.OrdinalIgnoreCase);
        // El cursor ignora el resto de filtros sueltos.
        Assert.DoesNotContain("ignorada", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inbox", handler.Requests[0].AbsolutePath, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        var coverage = doc.RootElement.GetProperty("coverage")[0];
        var nextCursor = coverage.GetProperty("cursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nextCursor));
        var decoded = MailSearchTools.TryDecodeCursor(nextCursor);
        Assert.NotNull(decoded);
        Assert.Equal(Mailbox, decoded!.User);
        Assert.Equal("sent", decoded.Folder);
        Assert.Equal(52, decoded.Skip); // 50 iniciales + 2 mensajes leídos
    }

    [Fact]
    public async Task Query_Search_NeverSendsSkip_And_NoResumeCursor()
    {
        var (graph, handler) = CreateGraph(_ => Page(null));

        var json = await MailSearchTools.SearchHits(
            graph, NullLogger<MailSearchTools>.Instance, Context(),
            userIds: Mailbox, folders: "sent", ranges: "2026-10-01..2026-10-02",
            queries: "desvinculacion", maxCalls: 12);

        Assert.NotEmpty(handler.Requests);
        var q = Query(handler.Requests[0]);
        Assert.Contains("$search", q, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$skip", q, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        var coverage = doc.RootElement.GetProperty("coverage")[0];
        Assert.Equal("done", coverage.GetProperty("status").GetString());
        Assert.False(coverage.TryGetProperty("cursor", out _));
    }

    [Fact]
    public async Task InvalidCursor_FailsBeforeAnyGraphCall()
    {
        var (graph, handler) = CreateGraph(_ => Page(null));

        var json = await MailSearchTools.SearchHits(
            graph, NullLogger<MailSearchTools>.Instance, Context(),
            userIds: Mailbox, cursor: "no-es-un-cursor");

        Assert.Empty(handler.Requests);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("cursor", doc.RootElement.GetProperty("error").GetString());
    }
}
