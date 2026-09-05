using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using ZeroTeamsMcp.Core.Models;

var baseUrlOpt = new Option<string>("--url", () => "http://localhost:5100", "ControlPlane URL");
var tenantIdOpt = new Option<string>("--tenant-id", "Azure AD Tenant ID");
var clientIdOpt = new Option<string>("--client-id", "App Registration Client ID");
var secretOpt = new Option<string>("--client-secret", "App Registration Client Secret");
var displayNameOpt = new Option<string>("--display-name", "Tenant display name");

var addCmd = new Command("tenant-add", "Registrar un nuevo tenant en ControlPlane")
{
    tenantIdOpt, clientIdOpt, secretOpt, displayNameOpt, baseUrlOpt
};
addCmd.SetHandler(async (tenantId, clientId, clientSecret, displayName, baseUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var config = new TenantConfig
    {
        TenantId = tenantId,
        ClientId = clientId,
        ClientSecret = clientSecret,
        DisplayName = displayName
    };
    var resp = await http.PostAsJsonAsync("/api/tenants", config);
    if (resp.IsSuccessStatusCode)
    {
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"Tenant registrado: {tenantId}");
        Console.WriteLine($"Consent URL: {result.GetProperty("consentUrl").GetString()}");
    }
    else
    {
        var err = await resp.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"Error: {resp.StatusCode} — {err}");
    }
}, tenantIdOpt, clientIdOpt, secretOpt, displayNameOpt, baseUrlOpt);

var listCmd = new Command("tenant-list", "Listar tenants registrados")
{
    baseUrlOpt
};
listCmd.SetHandler(async (baseUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var tenants = await http.GetFromJsonAsync<List<JsonElement>>("/api/tenants");
    if (tenants is null || tenants.Count == 0)
    {
        Console.WriteLine("No hay tenants registrados.");
        return;
    }
    Console.WriteLine($"{"TenantId",-38} {"Display",-20} Consent");
    Console.WriteLine(new string('-', 70));
    foreach (var t in tenants)
    {
        var id = t.GetProperty("tenantId").GetString() ?? "";
        var name = t.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
        var consent = t.TryGetProperty("consent", out var c) ? c.GetString() ?? "Pending" : "Pending";
        Console.WriteLine($"{id,-38} {name,-20} {consent}");
    }
}, baseUrlOpt);

var consentCmd = new Command("tenant-consent", "Marcar consentimiento como granted/revoked")
{
    tenantIdOpt, baseUrlOpt
};
var statusOpt = new Option<string>("--status", () => "Granted", "Consent status: Granted, Revoked");
consentCmd.AddOption(statusOpt);
consentCmd.SetHandler(async (tenantId, baseUrl, status) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var body = new { Status = status };
    var resp = await http.PostAsJsonAsync($"/api/tenants/{tenantId}/consent", body);
    if (resp.IsSuccessStatusCode)
        Console.WriteLine($"Consent for {tenantId} → {status}");
    else
        Console.Error.WriteLine($"Error: {resp.StatusCode}");
}, tenantIdOpt, baseUrlOpt, statusOpt);

var healthCmd = new Command("health", "Verificar estado del ControlPlane")
{
    baseUrlOpt
};
healthCmd.SetHandler(async (baseUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    try
    {
        var health = await http.GetFromJsonAsync<JsonElement>("/health");
        Console.WriteLine($"Status: {health.GetProperty("status").GetString()}");
        var tenants = health.GetProperty("tenants");
        Console.WriteLine($"Tenants: {tenants.GetProperty("total").GetInt32()} total, {tenants.GetProperty("consented").GetInt32()} consented");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ControlPlane unreachable: {ex.Message}");
    }
}, baseUrlOpt);

var deleteCmd = new Command("tenant-delete", "Eliminar un tenant")
{
    tenantIdOpt, baseUrlOpt
};
deleteCmd.SetHandler(async (tenantId, baseUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var resp = await http.DeleteAsync($"/api/tenants/{tenantId}");
    Console.WriteLine(resp.IsSuccessStatusCode
        ? $"Tenant {tenantId} eliminado."
        : $"Error: {resp.StatusCode}");
}, tenantIdOpt, baseUrlOpt);

var rootCmd = new RootCommand("ZeroTeams MCP CLI — orquestador multi-tenant")
{
    addCmd, listCmd, consentCmd, healthCmd, deleteCmd
};

return await rootCmd.InvokeAsync(args);
