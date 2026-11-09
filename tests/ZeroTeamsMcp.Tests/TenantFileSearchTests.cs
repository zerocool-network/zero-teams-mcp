using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Tests;

public class TenantFileSearchTests
{
    private static readonly RestrictedPolicy Policy = RestrictedPolicy.Parse(
        "excluido@example.com",
        null,
        null);

    [Fact]
    public void ConsultaVacia_Falla()
    {
        Assert.False(TenantFileSearch.TryRequest("  ", 25, out _, out var error));
        Assert.Equal("query es obligatorio.", error);
    }

    [Fact]
    public void TopeCincuenta_RegionEmea()
    {
        Assert.True(TenantFileSearch.TryRequest("  filename:informe.pdf  ", 80, out var request, out var error));
        Assert.Null(error);
        Assert.Equal("filename:informe.pdf", request.QueryString);
        Assert.Equal(50, request.Size);
        Assert.Equal("EMEA", request.Region);
    }

    [Fact]
    public void TopNoPositivo_UsaElDefecto()
    {
        Assert.True(TenantFileSearch.TryRequest("filename:informe.pdf", 0, out var request, out _));
        Assert.Equal(25, request.Size);
        Assert.Equal(1, TenantFileSearch.TryRequest("filename:informe.pdf", 1, out var one, out _) ? one.Size : 0);
    }

    [Fact]
    public void Extension_IgnoraMayusculasYPunto()
    {
        Assert.True(TenantFileSearch.MatchesFileType("Informe.PDF", "pdf"));
        Assert.True(TenantFileSearch.MatchesFileType("Informe.PDF", ".PDF"));
        Assert.False(TenantFileSearch.MatchesFileType("Informe.PDF.bak", "pdf"));
        Assert.True(TenantFileSearch.MatchesFileType("Informe.PDF", null));
    }

    [Fact]
    public void CarpetaSinExtension_SeDescarta()
    {
        Assert.False(TenantFileSearch.MatchesFileType("Informes", "pdf"));
        Assert.False(TenantFileSearch.MatchesFileType(null, "pdf"));
    }

    [Fact]
    public void Restringido_SinEmail_SeOculta()
    {
        Assert.True(TenantFileSearch.HideFromRestricted(true, Policy, null));
        Assert.True(TenantFileSearch.HideFromRestricted(true, Policy, "  "));
    }

    [Fact]
    public void Restringido_EmailExcluido_SeOculta()
    {
        Assert.True(TenantFileSearch.HideFromRestricted(true, Policy, "Excluido@example.com"));
        Assert.False(TenantFileSearch.HideFromRestricted(true, Policy, "otra@example.com"));
    }

    [Fact]
    public void Dios_NoFiltra()
    {
        Assert.False(TenantFileSearch.HideFromRestricted(false, Policy, null));
        Assert.False(TenantFileSearch.HideFromRestricted(false, Policy, "excluido@example.com"));
    }

    [Fact]
    public void Restringido_SinDenylist_OcultaTodo()
    {
        var empty = RestrictedPolicy.Parse(null, null, null);
        Assert.True(TenantFileSearch.HideFromRestricted(true, empty, "otra@example.com"));
    }
}
