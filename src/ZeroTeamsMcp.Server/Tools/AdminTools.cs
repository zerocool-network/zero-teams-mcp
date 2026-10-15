using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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

    private static string AdminToken => Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? "";

    private static void ValidateAdminToken(string? providedToken)
    {
        if (string.IsNullOrEmpty(AdminToken))
            throw new InvalidOperationException("ADMIN_TOKEN not configured on server");
        if (!string.Equals(providedToken, AdminToken, StringComparison.Ordinal))
            throw new McpException("Token de administrador inválido.");
    }

    [McpServerTool(Name = "admin_list_tenants")]
    [Description("Lista todos los tenants registrados en el ControlPlane. Requiere adminToken.")]
    public static async Task<string> ListTenants(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_list_tenants", null, async () =>
        {
            ValidateAdminToken(adminToken);
            var url = controlPlaneUrl ?? "http://localhost:5100";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{url}/api/tenants");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<object>(json, JsonOpts)!;
        });
    }

    [McpServerTool(Name = "admin_get_tenant")]
    [Description("Obtiene detalles de un tenant específico por su ID. Requiere adminToken.")]
    public static async Task<string> GetTenant(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_get_tenant", null, async () =>
        {
            ValidateAdminToken(adminToken);
            var url = controlPlaneUrl ?? "http://localhost:5100";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{url}/api/tenants/{tenantId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new { error = "Tenant no encontrado" };
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<object>(await response.Content.ReadAsStringAsync(), JsonOpts)!;
        });
    }

    [McpServerTool(Name = "admin_create_tenant")]
    [Description("Registra un nuevo tenant en el ControlPlane. Requiere tenantId, clientId, clientSecret, displayName y adminToken.")]
    public static async Task<string> CreateTenant(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("Tenant ID de Entra ID (UUID).")] string tenantId,
        [Description("Client ID de la App Registration (UUID).")] string clientId,
        [Description("Client Secret de la App Registration.")] string clientSecret,
        [Description("Nombre descriptivo del tenant.")] string displayName,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_create_tenant", null, async () =>
        {
            ValidateAdminToken(adminToken);
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
                return new { error = "Tenant ya existe" };
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<object>(await response.Content.ReadAsStringAsync(), JsonOpts)!;
        });
    }

    [McpServerTool(Name = "admin_delete_tenant")]
    [Description("Elimina un tenant del ControlPlane por su ID. Requiere adminToken.")]
    public static async Task<string> DeleteTenant(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_delete_tenant", null, async () =>
        {
            ValidateAdminToken(adminToken);
            var url = controlPlaneUrl ?? "http://localhost:5100";
            var client = httpClientFactory.CreateClient();
            var response = await client.DeleteAsync($"{url}/api/tenants/{tenantId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new { error = "Tenant no encontrado" };
            response.EnsureSuccessStatusCode();
            return new { success = true, tenantId };
        });
    }

    [McpServerTool(Name = "admin_get_consent")]
    [Description("Obtiene el estado de consentimiento administrativo de un tenant. Requiere adminToken.")]
    public static async Task<string> GetConsent(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("Tenant ID (UUID).")] string tenantId,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_get_consent", null, async () =>
        {
            ValidateAdminToken(adminToken);
            var url = controlPlaneUrl ?? "http://localhost:5100";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{url}/api/tenants/{tenantId}/consent");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new { error = "Sin consent registrado", tenantId };
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<object>(await response.Content.ReadAsStringAsync(), JsonOpts)!;
        });
    }

    [McpServerTool(Name = "admin_health")]
    [Description("Verifica el estado del ControlPlane. Retorna status y conteo de tenants. Requiere adminToken.")]
    public static async Task<string> Health(
        IHttpClientFactory httpClientFactory,
        ILogger<AdminTools> logger,
        [Description("Token de administrador para autorización.")] string adminToken,
        [Description("URL del ControlPlane. Default: http://localhost:5100")] string? controlPlaneUrl = null)
    {
        return await logger.ExecuteWithLogging<object>("admin_health", null, async () =>
        {
            ValidateAdminToken(adminToken);
            var url = controlPlaneUrl ?? "http://localhost:5100";
            var client = httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{url}/health");
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<object>(await response.Content.ReadAsStringAsync(), JsonOpts)!;
        });
    }
}