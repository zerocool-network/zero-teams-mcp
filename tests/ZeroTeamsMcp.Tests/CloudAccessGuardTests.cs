using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ZeroTeamsMcp.Server.Services;
using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

/// <summary>
/// Cubre el hardening del modo cloud: los tools de escritura requieren admin y
/// la lectura de chats/calendario queda limitada al propio cliente.
/// </summary>
public class CloudAccessGuardTests
{
    private sealed class FakeAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClientContext Ctx(bool cloud, bool admin = false, string email = "")
    {
        var accessor = new FakeAccessor();
        if (cloud)
        {
            var http = new DefaultHttpContext();
            http.Items["CloudClient"] = new ClientIdentity { Email = email, IsAdmin = admin };
            accessor.HttpContext = http;
        }
        return new ClientContext(accessor);
    }

    private static readonly ClientContext RegularCtx = Ctx(cloud: true, admin: false, email: "user@contoso.com");
    private static readonly ClientContext AdminCtx = Ctx(cloud: true, admin: true, email: "*");
    private static readonly ClientContext StdioCtx = Ctx(cloud: false);

    private static readonly ILogger<TeamsChatTools> ChatLog = NullLogger<TeamsChatTools>.Instance;
    private static readonly ILogger<TeamsChannelTools> ChannelLog = NullLogger<TeamsChannelTools>.Instance;
    private static readonly ILogger<TeamsMemberTools> MemberLog = NullLogger<TeamsMemberTools>.Instance;
    private static readonly ILogger<TeamsCalendarTools> CalendarLog = NullLogger<TeamsCalendarTools>.Instance;

    // ---------------------------------------------------------------------
    // RequireAdmin
    // ---------------------------------------------------------------------

    [Fact]
    public void RequireAdmin_CloudRegularClient_Throws()
    {
        Assert.Throws<McpException>(
            () => CloudAccessGuard.RequireAdmin(RegularCtx, "teams_send_message"));
    }

    [Fact]
    public void RequireAdmin_CloudAdmin_DoesNotThrow()
    {
        CloudAccessGuard.RequireAdmin(AdminCtx, "teams_send_message");
    }

    [Fact]
    public void RequireAdmin_StdioWithoutIdentity_DoesNotThrow()
    {
        CloudAccessGuard.RequireAdmin(StdioCtx, "teams_send_message");
    }

    // ---------------------------------------------------------------------
    // EnsureChatMember (decisión pura)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("user@contoso.com", "user@contoso.com", true)]
    [InlineData("User@Contoso.com", "user@contoso.com", true)]
    [InlineData("user@contoso.com", "otro@contoso.com", false)]
    [InlineData("user@contoso.com", "", false)]
    public void EnsureChatMember_CloudRegular_CheckMembership(
        string clientEmail, string memberEmail, bool allowed)
    {
        var members = string.IsNullOrEmpty(memberEmail)
            ? Array.Empty<string?>()
            : new[] { memberEmail };

        if (allowed)
        {
            CloudAccessGuard.EnsureChatMember("t", "chat-1", true, false, clientEmail, members);
        }
        else
        {
            Assert.Throws<McpException>(
                () => CloudAccessGuard.EnsureChatMember("t", "chat-1", true, false, clientEmail, members));
        }
    }

    [Fact]
    public void EnsureChatMember_CloudAdmin_SkipsCheck()
    {
        CloudAccessGuard.EnsureChatMember("t", "chat-1", true, true, "admin@contoso.com", []);
    }

    [Fact]
    public void EnsureChatMember_Stdio_SkipsCheck()
    {
        CloudAccessGuard.EnsureChatMember("t", "chat-1", false, false, null, []);
    }

    [Fact]
    public void EnsureChatMember_CloudRegularWithoutEmail_Throws()
    {
        Assert.Throws<McpException>(
            () => CloudAccessGuard.EnsureChatMember("t", "chat-1", true, false, "", ["x@y.com"]));
    }

    // ---------------------------------------------------------------------
    // EnsureChatMemberAsync: admin y stdio retornan sin tocar Graph
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EnsureChatMemberAsync_Admin_ReturnsWithoutGraph()
    {
        await CloudAccessGuard.EnsureChatMemberAsync(null!, AdminCtx, "t", "chat-1", null);
    }

    [Fact]
    public async Task EnsureChatMemberAsync_Stdio_ReturnsWithoutGraph()
    {
        await CloudAccessGuard.EnsureChatMemberAsync(null!, StdioCtx, "t", "chat-1", null);
    }

    // ---------------------------------------------------------------------
    // Integración: las tools de escritura bloquean al cliente regular.
    // El guard se ejecuta antes de usar Graph/logger, por eso se pasan null!.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateChat_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsChatTools.CreateChat(null!, ChatLog, RegularCtx, "group", "id-1"));

    [Fact]
    public async Task SendMessage_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsChatTools.SendMessage(null!, ChatLog, RegularCtx, "chat-1", "hola"));

    [Fact]
    public async Task CreateChannel_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsChannelTools.CreateChannel(null!, ChannelLog, RegularCtx, "team-1", "canal"));

    [Fact]
    public async Task SendChannelMessage_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsChannelTools.SendChannelMessage(null!, ChannelLog, RegularCtx, "team-1", "ch-1", "hola"));

    [Fact]
    public async Task CreateCalendarEvent_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsCalendarTools.CreateCalendarEvent(
                null!, CalendarLog, RegularCtx, "user@contoso.com", "s",
                "2026-01-01T00:00:00Z", "2026-01-01T01:00:00Z"));

    [Fact]
    public async Task DeleteCalendarEvent_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsCalendarTools.DeleteCalendarEvent(null!, CalendarLog, RegularCtx, "user@contoso.com", "ev-1"));

    [Fact]
    public async Task AddChatMember_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsMemberTools.AddChatMember(null!, MemberLog, RegularCtx, "chat-1", "user-1"));

    [Fact]
    public async Task RemoveChatMember_RegularClient_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsMemberTools.RemoveChatMember(null!, MemberLog, RegularCtx, "chat-1", "mem-1"));

    // ---------------------------------------------------------------------
    // Scoping de lectura del calendario: cliente regular solo su propio email
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ListCalendarEvents_RegularClient_OtherUser_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsCalendarTools.ListCalendarEvents(null!, CalendarLog, RegularCtx, "otro@contoso.com"));

    [Fact]
    public async Task GetCalendarEvent_RegularClient_OtherUser_Throws()
        => await Assert.ThrowsAsync<McpException>(
            () => TeamsCalendarTools.GetCalendarEvent(null!, CalendarLog, RegularCtx, "otro@contoso.com", "ev-1"));

    // ---------------------------------------------------------------------
    // Contraprueba: admin y stdio NO son bloqueados por el guard
    // (fallan más tarde por el Graph null, pero nunca por autorización).
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateChat_AdminClient_NotBlockedByGuard()
    {
        var ex = await Record.ExceptionAsync(
            () => TeamsChatTools.CreateChat(null!, ChatLog, AdminCtx, "group", "id-1"));

        Assert.IsNotType<McpException>(ex);
    }

    [Fact]
    public async Task CreateChat_StdioClient_NotBlockedByGuard()
    {
        var ex = await Record.ExceptionAsync(
            () => TeamsChatTools.CreateChat(null!, ChatLog, StdioCtx, "group", "id-1"));

        Assert.IsNotType<McpException>(ex);
    }

    [Fact]
    public async Task ListCalendarEvents_RegularClient_OwnEmail_NotBlockedByScoping()
    {
        var ex = await Record.ExceptionAsync(
            () => TeamsCalendarTools.ListCalendarEvents(null!, CalendarLog, RegularCtx, "user@contoso.com"));

        Assert.IsNotType<McpException>(ex);
    }
}
