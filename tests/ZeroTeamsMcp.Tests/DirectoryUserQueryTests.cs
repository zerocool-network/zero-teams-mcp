using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Tests;

public class DirectoryUserQueryTests
{
    private static readonly RestrictedPolicy Policy = RestrictedPolicy.Parse(
        "excluido@example.com",
        null,
        null);

    [Fact]
    public void NombreConVariasPalabras_ExigeTodas()
    {
        Assert.True(DirectoryUserQuery.TrySearch("Ana Perez", out var search, out var error));
        Assert.Null(error);
        Assert.Equal("\"displayName:Ana AND displayName:Perez\"", search);
    }

    [Fact]
    public void Email_BuscaCorreoYUpn()
    {
        Assert.True(DirectoryUserQuery.TrySearch("ana.perez@optimaretail.es", out var search, out _));
        Assert.Equal(
            "\"mail:ana.perez@optimaretail.es OR userPrincipalName:ana.perez@optimaretail.es\"",
            search);
    }

    [Fact]
    public void ConsultaVacia_Falla()
    {
        Assert.False(DirectoryUserQuery.TrySearch("  ", out _, out var error));
        Assert.Equal("query es obligatorio.", error);
    }

    [Fact]
    public void Restringido_OcultaExcluidoYSinEmail()
    {
        Assert.True(DirectoryUserQuery.HideFromRestricted(true, Policy, "excluido@example.com", null));
        Assert.True(DirectoryUserQuery.HideFromRestricted(true, Policy, null, null));
        Assert.False(DirectoryUserQuery.HideFromRestricted(true, Policy, "otra@example.com", null));
    }

    [Fact]
    public void Dios_NoFiltra_YNextSoloEsGraph()
    {
        Assert.False(DirectoryUserQuery.HideFromRestricted(false, Policy, null, null));
        Assert.True(DirectoryUserQuery.IsGraphNext(null));
        Assert.True(DirectoryUserQuery.IsGraphNext("https://graph.microsoft.com/v1.0/users?$skiptoken=abc"));
        Assert.False(DirectoryUserQuery.IsGraphNext("https://example.com/users"));
    }
}
