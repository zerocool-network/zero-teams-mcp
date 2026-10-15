using ZeroTeamsMcp.ControlPlane.Endpoints;

namespace ZeroTeamsMcp.Tests;

/// <summary>
/// Cubre la página de éxito del callback OAuth. El script es inline y debe
/// postMessagear al panel con un JSON válido: un fallo aquí deja al popup sin
/// cerrar y al panel sin refrescar (lo detectó una prueba manual, no la suite).
/// </summary>
public class AuthCallbackPageTests
{
    [Fact]
    public void SuccessPage_GeneraJsValido()
    {
        var html = AuthEndpoints.SuccessPageHtml("u-1", "user@example.com", "https://panel.example.com");

        // Llaves simples (no crudas) y llamada completa.
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("}}", html);
        Assert.Contains("window.opener.postMessage({ type: 'auth-success'", html);
        Assert.Contains("window.close()", html);
    }

    [Fact]
    public void SuccessPage_SerializaValoresComoJson()
    {
        var html = AuthEndpoints.SuccessPageHtml("u-1", "user@example.com", "https://panel.example.com");

        Assert.Contains("userId: \"u-1\"", html);
        Assert.Contains("email: \"user@example.com\"", html);
        Assert.Contains("\"https://panel.example.com\"", html);
    }

    [Fact]
    public void SuccessPage_EscapaHtmlEnElEmail()
    {
        var html = AuthEndpoints.SuccessPageHtml("u-1", "<script>alert(1)</script>@x.com", "https://p.example.com");

        // En el cuerpo HTML va escapado...
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<p>User: <script>", html);
        // ...y en el JS va como string JSON (sin romper el literal).
        Assert.DoesNotContain("email: <script>", html);
    }

    [Fact]
    public void SuccessPage_EmailConComillasNoRompeElJs()
    {
        var html = AuthEndpoints.SuccessPageHtml("u-1", "a\"b@example.com", "https://p.example.com");

        // La comilla va escapada (JSON: \u0022 o \"), nunca cruda dentro del literal.
        Assert.DoesNotContain("email: \"a\"b@example.com\"", html);
        Assert.True(
            html.Contains("email: \"a\\u0022b@example.com\"") || html.Contains("email: \"a\\\"b@example.com\""),
            $"Comilla sin escapar en el JS:\n{html}");
    }
}
