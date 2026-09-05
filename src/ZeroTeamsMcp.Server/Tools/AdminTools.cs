using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace ZeroTeamsMcp.Server.Tools;

[McpServerToolType]
public sealed class AdminTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [McpServerTool(Name = "admin_list_tenants")]
    public static async Task<string> ListTenants(
        IHttpClientFactory httpClientFactory,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{url}/api/tenants");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return json;
    }

    [McpServerTool(Name = "admin_get_tenant")]
    public static async Task<string> GetTenant(
        IHttpClientFactory httpClientFactory,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{url}/api/tenants/{tenantId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return JsonSerializer.Serialize(new { error = "Tenant no encontrado" }, JsonOpts);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "admin_create_tenant")]
    public static async Task<string> CreateTenant(
        IHttpClientFactory httpClientFactory,
        [Description("Tenant ID de Entra ID (UUID).")] string tenantId,
        [Description("Client ID de la App Registration (UUID).")] string clientId,
        [Description("Client Secret de la App Registration.")] string clientSecret,
        [Description("Nombre descriptivo del tenant.")] string displayName,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var body = JsonSerializer.Serialize(new
        {
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = displayName
        }, JsonOpts);
        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{url}/api/tenants", content);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return JsonSerializer.Serialize(new { error = "Tenant ya existe" }, JsonOpts);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "admin_delete_tenant")]
    public static async Task<string> DeleteTenant(
        IHttpClientFactory httpClientFactory,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var response = await client.DeleteAsync($"{url}/api/tenants/{tenantId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return JsonSerializer.Serialize(new { error = "Tenant no encontrado" }, JsonOpts);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Serialize(new { success = true, tenantId }, JsonOpts);
    }

    [McpServerTool(Name = "admin_get_consent")]
    public static async Task<string> GetConsent(
        IHttpClientFactory httpClientFactory,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{url}/api/tenants/{tenantId}/consent");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return JsonSerializer.Serialize(new { error = "Sin consent registrado", tenantId }, JsonOpts);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [McpServerTool(Name = "admin_health")]
    public static async Task<string> Health(
        IHttpClientFactory httpClientFactory,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        var url = controlPlaneUrl ?? "http://localhost:5100";
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{url}/health");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
