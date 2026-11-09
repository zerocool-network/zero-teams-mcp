using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Tests;

public class MailListQueryTests
{
    [Fact]
    public void CarpetaEnviados_UsaSentItems()
    {
        Assert.True(MailListQuery.TryFolder("enviados", out var id, out var error));
        Assert.Equal("sentitems", id);
        Assert.Null(error);
        Assert.Equal("sentDateTime", MailListQuery.DateField(id));
    }

    [Fact]
    public void CarpetaVacia_EsTodoElBuzon()
    {
        Assert.True(MailListQuery.TryFolder(null, out var id, out var error));
        Assert.Null(id);
        Assert.Null(error);
        Assert.Equal("receivedDateTime", MailListQuery.DateField(id));
    }

    [Fact]
    public void CarpetaDesconocida_Falla()
    {
        Assert.False(MailListQuery.TryFolder("personal", out _, out var error));
        Assert.Contains("inbox", error);
    }

    [Fact]
    public void Fechas_CubrenElDiaEnMadrid()
    {
        Assert.True(MailListQuery.TryFilter("2026-10-13", "2026-10-27", "receivedDateTime", out var filter, out var error));
        Assert.Null(error);
        Assert.Equal(
            "receivedDateTime ge 2026-10-12T22:00:00Z and receivedDateTime le 2026-10-27T22:59:59Z",
            filter);
    }

    [Fact]
    public void Enviados_FiltranPorSentDateTime()
    {
        Assert.True(MailListQuery.TryFilter("2026-10-23", null, "sentDateTime", out var filter, out _));
        Assert.Equal("sentDateTime ge 2026-10-22T22:00:00Z", filter);
    }

    [Fact]
    public void Busqueda_AcotaRemitenteFechaYAdjuntos()
    {
        Assert.True(MailListQuery.TrySearch(
            "2026-10-13",
            "2026-10-27",
            "ana.perez@optimaretail.es",
            "DILLE & KAMILLE",
            true,
            "sent",
            out var search,
            out var error));
        Assert.Null(error);
        Assert.Equal(
            "\"from:ana.perez@optimaretail.es AND subject:\\\"DILLE & KAMILLE\\\" AND hasAttachments:true AND sent>=2026-10-13 AND sent<=2026-10-27\"",
            search);
    }

    [Fact]
    public void Busqueda_DestinatarioYTextoLibre()
    {
        Assert.True(MailListQuery.TrySearch(
            "2026-08-27",
            "2026-10-27",
            null,
            null,
            null,
            "sent",
            out var search,
            out var error,
            "impulsa.com",
            "impulsa"));
        Assert.Null(error);
        Assert.Equal(
            "\"recipients:impulsa.com AND impulsa AND sent>=2026-08-27 AND sent<=2026-10-27\"",
            search);
    }

    [Fact]
    public void Busqueda_FraseExacta_EscapaComillas()
    {
        Assert.True(MailListQuery.TrySearch(
            null, null, null, null, null, "received", out var search, out var error, null, "cambio de fecha"));
        Assert.Null(error);
        Assert.Equal("\"\\\"cambio de fecha\\\"\"", search);
        Assert.DoesNotContain("body:\"", search);
    }

    [Fact]
    public void DestinatarioSinDominio_Falla()
    {
        Assert.False(MailListQuery.TrySearch(
            null, null, null, null, null, "received", out _, out var error, "impulsa"));
        Assert.Contains("toAddress", error);
    }

    [Fact]
    public void Before_SigueHaciaAtras()
    {
        Assert.True(MailListQuery.TryFilter(null, null, "receivedDateTime", out var filter, out _, "2026-10-26T22:50:00Z"));
        Assert.Equal("receivedDateTime lt 2026-10-26T22:50:00Z", filter);
    }

    [Fact]
    public void SincePosterior_Falla()
    {
        Assert.False(MailListQuery.TryFilter("2026-10-27", "2026-10-13", "receivedDateTime", out _, out var error));
        Assert.Contains("sinceDate", error);
    }
}
