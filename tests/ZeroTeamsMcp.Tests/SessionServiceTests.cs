using Microsoft.AspNetCore.Http;
using ZeroTeamsMcp.ControlPlane.Services;

namespace ZeroTeamsMcp.Tests;

public class SessionServiceTests
{
    private static SessionService NewService(TimeSpan? ttl = null) =>
        new("test-secret-para-sesion", ttl ?? TimeSpan.FromHours(1));

    [Fact]
    public void Create_Verify_Roundtrip()
    {
        var svc = NewService();
        var token = svc.Create("user-123", "Ana.Perez@Example.com");

        var info = svc.Verify(token);

        Assert.NotNull(info);
        Assert.Equal("user-123", info!.UserId);
        Assert.Equal("Ana.Perez@Example.com", info.Email);
        Assert.True(info.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Verify_RejectsTamperedToken()
    {
        var svc = NewService();
        var token = svc.Create("user-123", "ana@example.com");
        var parts = token.Split('.');
        // Firma manipulada
        Assert.Null(svc.Verify(parts[0] + "." + parts[1][..^2] + "AA"));
        // Payload manipulado
        Assert.Null(svc.Verify("ZmFrZQ." + parts[1]));
    }

    [Fact]
    public void Verify_RejectsTokenFromDifferentSecret()
    {
        var a = NewService();
        var b = new SessionService("otro-secreto", TimeSpan.FromHours(1));
        Assert.Null(b.Verify(a.Create("u", "e@e.com")));
    }

    [Fact]
    public void Verify_RejectsExpiredToken()
    {
        var svc = NewService(TimeSpan.FromSeconds(-10));
        Assert.Null(svc.Verify(svc.Create("u", "e@e.com")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-punto")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    public void Verify_RejectsMalformedTokens(string? token)
    {
        Assert.Null(NewService().Verify(token));
    }

    [Fact]
    public void Cookie_Roundtrip_Y_Lectura()
    {
        var svc = NewService();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        svc.WriteCookie(ctx, svc.Create("user-9", "user9@example.com"));
        var setCookie = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains(SessionService.CookieName, setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

        // Simular la siguiente petición con esa cookie
        var value = svc.Create("user-9", "user9@example.com");
        var next = new DefaultHttpContext();
        next.Request.Headers.Cookie = $"{SessionService.CookieName}={value}";

        var info = svc.Read(next);
        Assert.NotNull(info);
        Assert.Equal("user9@example.com", info!.Email);
    }

    [Fact]
    public void Read_SinCookie_DevuelveNull()
    {
        Assert.Null(NewService().Read(new DefaultHttpContext()));
    }

    [Fact]
    public void Cookie_EsSecure_ConXForwardedProtoHttps()
    {
        var svc = NewService();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";

        svc.WriteCookie(ctx, svc.Create("u", "e@e.com"));

        Assert.Contains("secure", ctx.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cookie_NoSecure_EnHttpDirecto()
    {
        var svc = NewService();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        svc.WriteCookie(ctx, svc.Create("u", "e@e.com"));

        Assert.DoesNotContain("secure", ctx.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAdminEmail_UsaListaDeEnv()
    {
        var previous = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
        try
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAILS", "Admin@Example.com, otro@example.com");

            Assert.True(SessionService.IsAdminEmail("admin@example.com"));
            Assert.True(SessionService.IsAdminEmail("OTRO@EXAMPLE.COM"));
            Assert.False(SessionService.IsAdminEmail("cliente@example.com"));
            Assert.False(SessionService.IsAdminEmail(""));
            Assert.False(SessionService.IsAdminEmail(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAILS", previous);
        }
    }

    [Fact]
    public void IsAdminEmail_SinEnv_NadieEsAdmin()
    {
        var previous = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
        try
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAILS", null);
            Assert.False(SessionService.IsAdminEmail("admin@example.com"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAILS", previous);
        }
    }
}
