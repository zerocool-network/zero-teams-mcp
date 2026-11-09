using ZeroTeamsMcp.Server.Services;
using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

public class TokenEfficiencyTests
{
    [Fact]
    public void DevDiagnostics_ApproxResponseChars()
    {
        Assert.Equal(0, DevDiagnostics.ApproxResponseChars(null));
        Assert.Equal(0, DevDiagnostics.ApproxResponseChars(""));
        Assert.Equal(5, DevDiagnostics.ApproxResponseChars("hello"));
    }

    [Fact]
    public void MailSearch_ParseRanges_Json()
    {
        var r = MailSearchTools.ParseRanges("[{\"since\":\"2026-10-15\",\"until\":\"2026-10-31\"},{\"since\":\"2026-11-01\",\"until\":\"2026-11-08\"}]");
        Assert.Equal(2, r.Count);
        Assert.Equal("2026-10-15", r[0].Since);
        Assert.Equal("2026-10-31", r[0].Until);
        Assert.Null(r[0].Error);
    }

    [Fact]
    public void MailSearch_ParseRanges_Text()
    {
        var r = MailSearchTools.ParseRanges("2026-10-15..2026-10-31;2026-11-01..2026-11-08");
        Assert.Equal(2, r.Count);
        Assert.Equal("2026-11-01", r[1].Since);
        Assert.Equal("2026-11-08", r[1].Until);
    }

    [Fact]
    public void MailSearch_SplitList()
    {
        var s = MailSearchTools.SplitList("a@x.com; b@x.com, c@x.com");
        Assert.Equal(3, s.Count);
        Assert.Contains("b@x.com", s);
    }

    [Fact]
    public void MailSearch_Cursor_Roundtrip()
    {
        var c = MailSearchTools.EncodeCursor("ana@x.com", "sent", "2026-10-01", "2026-10-31", "", 50);
        Assert.DoesNotContain('=', c);
        var d = MailSearchTools.TryDecodeCursor(c);
        Assert.NotNull(d);
        Assert.Equal("ana@x.com", d!.User);
        Assert.Equal("sent", d.Folder);
        Assert.Equal("2026-10-01", d.Since);
        Assert.Equal("2026-10-31", d.Until);
        Assert.Equal("", d.Query);
        Assert.Equal(50, d.Skip);
    }

    [Fact]
    public void MailSearch_Cursor_Invalid_ReturnsNull()
    {
        Assert.Null(MailSearchTools.TryDecodeCursor("no-es-base64!!"));
        Assert.Null(MailSearchTools.TryDecodeCursor(""));
        Assert.Null(MailSearchTools.TryDecodeCursor("eyJ4IjoxfQ")); // {"x":1}
    }

    [Fact]
    public void MailFields_Whitelist()
    {
        var set = MailDriveTools.ParseMailFields("id,subject,bogus,from");
        Assert.NotNull(set);
        Assert.Contains("id", set!);
        Assert.Contains("from", set);
        Assert.DoesNotContain("bogus", set);
    }
}
