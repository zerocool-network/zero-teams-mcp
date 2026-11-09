using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Tests;

public class RestrictedAccessGuardTests
{
    private const string Denied = "anas.chatt@optimaretail.es";
    private const string Allowed = "otra.persona@optimaretail.es";
    private const string TeamName = "IT (INMIGRANTES) IT";

    private static readonly RestrictedPolicy Policy = RestrictedPolicy.Parse(
        $"{Denied},julie.villegas@optimaretail.es",
        TeamName,
        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private sealed class FakeAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static ClientContext Ctx(bool admin = false, string role = "", string email = "")
    {
        var accessor = new FakeAccessor();
        var http = new DefaultHttpContext();
        http.Items["CloudClient"] = new ClientIdentity
        {
            Email = email,
            IsAdmin = admin,
            Role = role
        };
        accessor.HttpContext = http;
        return new ClientContext(accessor);
    }

    private static ClientContext Restricted() => Ctx(role: "restricted", email: "restricted");
    private static ClientContext God() => Ctx(admin: true, role: "admin", email: "*");

    [Fact]
    public void God_ConEmailExcluido_Pasa()
    {
        RestrictedAccessGuard.EnsureUserFacts(God(), "teams_list_chats", Denied, Denied, null, Policy);
        RestrictedAccessGuard.EnsureTeamFacts(God(), "teams_list_teams", TeamName, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", Policy);
        RestrictedAccessGuard.EnsureChatFacts(God(), "teams_get_chat_messages", unresolvedMember: true, [Denied], Policy);
    }

    [Fact]
    public void Restricted_EmailExcluido_Deniega()
    {
        var ex = Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureUserFacts(Restricted(), "teams_list_chats", Denied, Allowed, null, Policy));
        Assert.Contains("excluida", ex.Message);
    }

    [Fact]
    public void Restricted_GuidResueltoACorreoExcluido_Deniega()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureUserFacts(
                Restricted(),
                "teams_list_chats",
                mail: null,
                upn: "otro@optimaretail.es",
                proxyAddresses: ["SMTP:" + Denied],
                Policy));
    }

    [Fact]
    public void Restricted_UsuarioPermitido_Pasa()
    {
        RestrictedAccessGuard.EnsureUserFacts(Restricted(), "teams_list_chats", Allowed, Allowed, null, Policy);
    }

    [Fact]
    public void Restricted_SinDireccion_Deniega()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureUserFacts(Restricted(), "teams_list_chats", null, null, null, Policy));
    }

    [Fact]
    public void Restricted_DenylistVacia_DeniegaTodo()
    {
        var empty = RestrictedPolicy.Parse("   ", null, null);
        Assert.False(empty.EmailsConfigured);
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureConfigured(Restricted(), "teams_list_teams", empty));
    }

    [Fact]
    public void Restricted_NombreDeEquipoExacto_Deniega()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureTeamFacts(Restricted(), "teams_list_channels", "  " + TeamName + "  ", "otro-id", Policy));
    }

    [Fact]
    public void Restricted_DisplayNameNulo_Deniega()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureTeamFacts(Restricted(), "teams_list_channels", null, "otro-id", Policy));
    }

    [Fact]
    public void Restricted_TeamIdConfigurado_DeniegaAunqueCambieElNombre()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureTeamFacts(
                Restricted(), "teams_list_channels", "Nombre nuevo", "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", Policy));
    }

    [Fact]
    public void Restricted_OtroEquipo_Pasa()
    {
        RestrictedAccessGuard.EnsureTeamFacts(Restricted(), "teams_list_channels", "Retail", "11111111-1111-1111-1111-111111111111", Policy);
    }

    [Fact]
    public void Restricted_ChatConMiembroExcluido_DeniegaAunqueSeConozcaElId()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureChatFacts(
                Restricted(), "teams_get_chat_messages", unresolvedMember: false, [Allowed, Denied], Policy));
    }

    [Fact]
    public void Restricted_ChatConMiembroSinEmail_Deniega()
    {
        Assert.Throws<McpException>(() =>
            RestrictedAccessGuard.EnsureChatFacts(
                Restricted(), "teams_get_chat_messages", unresolvedMember: true, [Allowed], Policy));
    }

    [Fact]
    public void Restricted_EventoConAsistenteExcluido_QuedaOculto()
    {
        Assert.True(RestrictedAccessGuard.EventIsHidden(Restricted(), Allowed, [Allowed, Denied], Policy));
        Assert.False(RestrictedAccessGuard.EventIsHidden(Restricted(), Allowed, [Allowed], Policy));
        Assert.False(RestrictedAccessGuard.EventIsHidden(God(), Allowed, [Denied], Policy));
    }

    [Fact]
    public void Restricted_MensajeConCuentaExcluida_QuedaOculto()
    {
        Assert.True(RestrictedAccessGuard.MailIsHidden(Restricted(), Denied, [Allowed], Policy));
        Assert.True(RestrictedAccessGuard.MailIsHidden(Restricted(), Allowed, [Denied], Policy));
        Assert.False(RestrictedAccessGuard.MailIsHidden(Restricted(), Allowed, [Allowed], Policy));
        Assert.False(RestrictedAccessGuard.MailIsHidden(God(), Denied, [Denied], Policy));
    }

    [Fact]
    public void ResolveTarget_Restricted_AceptaOtroUsuario()
    {
        var target = Restricted().ResolveTarget("otra.persona@optimaretail.es");
        Assert.Equal("otra.persona@optimaretail.es", target);
    }

    [Fact]
    public void ResolveTarget_ClienteRegular_RechazaOtroUsuario()
    {
        var regular = Ctx(email: "user@contoso.com", role: "client");
        Assert.Throws<McpException>(() => regular.ResolveTarget("otro@contoso.com"));
    }

    [Fact]
    public void RequireAdmin_Restricted_DeniegaEscritura()
    {
        Assert.Throws<McpException>(() =>
            CloudAccessGuard.RequireAdmin(Restricted(), "teams_send_message"));
    }

    [Fact]
    public void ReadOnly_Restricted_OcultaEnvio_YDejaLectura()
    {
        var restricted = new ClientIdentity { Role = "restricted", IsAdmin = false, Email = "restricted" };
        Assert.True(ReadOnlyToolFilter.HidesTool("teams_send_message", restricted));
        Assert.True(ReadOnlyToolFilter.HidesTool("teams_create_chat", restricted));
        Assert.False(ReadOnlyToolFilter.HidesTool("teams_list_chats", restricted));
        Assert.False(ReadOnlyToolFilter.HidesTool("teams_get_chat_messages", restricted));
        Assert.False(ReadOnlyToolFilter.HidesTool("mail_list_messages", restricted));
        Assert.False(ReadOnlyToolFilter.HidesTool("onedrive_list_items", restricted));
    }

    [Fact]
    public void ReadOnly_AdminYStdio_ConservanEnvio()
    {
        var admin = new ClientIdentity { Role = "admin", IsAdmin = true, Email = "*" };
        Assert.False(ReadOnlyToolFilter.HidesTool("teams_send_message", admin));
        Assert.False(ReadOnlyToolFilter.HidesTool("teams_send_message", null));
    }
}
