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

// --- register command (cloud) ---
// El registro exige siempre ADMIN_TOKEN (por defecto lo lee de la env ADMIN_TOKEN):
// con registro abierto cualquiera podía registrarse como cualquier email y leer sus chats.
var registerEmailOpt = new Option<string?>("--email", "Email del usuario Teams (obligatorio salvo --admin)");
var registerAdminTokenOpt = new Option<string?>("--admin-token", () => Environment.GetEnvironmentVariable("ADMIN_TOKEN"), "ADMIN_TOKEN del ControlPlane (por defecto: env ADMIN_TOKEN)");
var registerAdminOpt = new Option<bool>("--admin", () => false, "Registrar como cliente admin (wildcard *). Ignora --email.");
var registerCmd = new Command("register", "Registrar un cliente remoto en el cloud y obtener su API key")
{
    registerEmailOpt, registerAdminTokenOpt, registerAdminOpt, baseUrlOpt
};
registerCmd.SetHandler(async (email, adminToken, admin, baseUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var resp = await http.PostAsJsonAsync("/api/clients/register", new { Email = email, AdminToken = adminToken, Admin = admin });
    if (!resp.IsSuccessStatusCode)
    {
        var err = await resp.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"Error: {resp.StatusCode} — {err}");
        return;
    }
    var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
    var apiKey = r.GetProperty("apiKey").GetString() ?? "";
    var serverUrl = r.GetProperty("serverUrl").GetString() ?? "";
    var regEmail = r.GetProperty("email").GetString() ?? "";
    var isAdmin = r.GetProperty("isAdmin").GetBoolean();
    Console.WriteLine($"Cliente registrado{(isAdmin ? " (ADMIN)" : "")}:");
    Console.WriteLine($"  Email   : {regEmail}");
    Console.WriteLine($"  API Key : {apiKey}");
    Console.WriteLine("  (guárdala — no se vuelve a mostrar)");
    Console.WriteLine();
    Console.WriteLine("Claude Desktop config (claude_desktop_config.json):");
    var cfg = new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["teams"] = new
            {
                url = serverUrl,
                headers = new Dictionary<string, string> { ["X-Api-Key"] = apiKey }
            }
        }
    };
    Console.WriteLine(JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
}, registerEmailOpt, registerAdminTokenOpt, registerAdminOpt, baseUrlOpt);

// --- test command ---
var mcpUrlOpt = new Option<string>("--mcp-url", () => "http://localhost:5200", "MCP Server URL");
var toolArg = new Argument<string>("tool", "Tool name (e.g. teams_list_teams)");
var argsArg = new Option<string>("--args", () => "{}", "JSON arguments for the tool");
var jsonOnlyOpt = new Option<bool>("--json", "Show raw JSON only");
var tableOpt = new Option<bool>("--table", "Show formatted table (default for lists)");

var testCmd = new Command("test", "Probar una herramienta MCP contra el servidor")
{
    toolArg, argsArg, mcpUrlOpt, jsonOnlyOpt, tableOpt
};
testCmd.SetHandler(async (tool, argsJson, mcpUrl, jsonOnly, table) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(mcpUrl) };
    http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

    var request = new { jsonrpc = "2.0", id = 1, method = "tools/call",
        @params = new { name = tool, arguments = JsonSerializer.Deserialize<JsonElement>(argsJson) } };

    try
    {
        var resp = await http.PostAsJsonAsync("/mcp", request);
        var body = await resp.Content.ReadAsStringAsync();

        // Parse SSE if needed
        if (body.StartsWith("event:"))
        {
            var lines = body.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data:"))
                {
                    body = line[5..].Trim();
                    break;
                }
            }
        }

        var result = JsonSerializer.Deserialize<JsonElement>(body);

        if (jsonOnly)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        // Extract content from MCP response
        if (result.TryGetProperty("result", out var rpcResult) &&
            rpcResult.TryGetProperty("content", out var content) &&
            content.GetArrayLength() > 0)
        {
            var text = content[0].GetProperty("text").GetString() ?? "";
            var parsedItems = new List<JsonElement>();
            var isTable = table || (!jsonOnly && TryParseJsonArray(text, out parsedItems));
            if (isTable)
            {
                PrintTable(parsedItems);
            }
            else
            {
                Console.WriteLine(text);
            }
        }
        else if (result.TryGetProperty("error", out var err))
        {
            Console.Error.WriteLine($"MCP Error: {err}");
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error connecting to MCP server: {ex.Message}");
    }
}, toolArg, argsArg, mcpUrlOpt, jsonOnlyOpt, tableOpt);

// --- tools command (list available tools) ---
var toolsCmd = new Command("tools", "Listar herramientas MCP disponibles")
{
    mcpUrlOpt
};
toolsCmd.SetHandler(async (mcpUrl) =>
{
    using var http = new HttpClient { BaseAddress = new Uri(mcpUrl) };
    http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

    var request = new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } };
    try
    {
        var resp = await http.PostAsJsonAsync("/mcp", request);
        var body = await resp.Content.ReadAsStringAsync();
        if (body.StartsWith("event:"))
        {
            var lines = body.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data:")) { body = line[5..].Trim(); break; }
            }
        }
        var result = JsonSerializer.Deserialize<JsonElement>(body);
        if (result.TryGetProperty("result", out var r) && r.TryGetProperty("tools", out var tools))
        {
            Console.WriteLine($"{"Tool",-35} Description");
            Console.WriteLine(new string('-', 80));
            foreach (var t in tools.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString() ?? "";
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                if (desc.Length > 42) desc = desc[..42] + "...";
                Console.WriteLine($"{name,-35} {desc}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }
}, mcpUrlOpt);

var rootCmd = new RootCommand("ZeroTeams MCP CLI — orquestador multi-tenant")
{
    addCmd, listCmd, consentCmd, healthCmd, deleteCmd, registerCmd, testCmd, toolsCmd
};

return await rootCmd.InvokeAsync(args);

// Helpers
static bool TryParseJsonArray(string text, out List<JsonElement> items)
{
    items = new();
    try
    {
        var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
                items.Add(item.Clone());
            return items.Count > 0;
        }
    }
    catch { }
    return false;
}

static void PrintTable(List<JsonElement> items)
{
    if (items.Count == 0) { Console.WriteLine("(empty)"); return; }

    // Detect keys from first item
    var keys = items[0].EnumerateObject().Select(p => p.Name).ToList();
    var widths = keys.ToDictionary(k => k, k => Math.Max(k.Length, 12));

    foreach (var item in items)
    {
        foreach (var key in keys)
        {
            if (item.TryGetProperty(key, out var val))
            {
                var s = val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
                if (s.Length > 40) s = s[..40] + "...";
                widths[key] = Math.Max(widths[key], s.Length);
            }
        }
    }

    // Header
    Console.WriteLine(string.Join("  ", keys.Select(k => k.PadRight(widths[k]))));
    Console.WriteLine(new string('-', widths.Values.Sum() + 2 * (keys.Count - 1)));

    // Rows
    foreach (var item in items)
    {
        var cells = keys.Select(key =>
        {
            if (item.TryGetProperty(key, out var val))
            {
                var s = val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
                if (s.Length > 40) s = s[..40] + "...";
                return s.PadRight(widths[key]);
            }
            return new string(' ', widths[key]);
        });
        Console.WriteLine(string.Join("  ", cells));
    }
    Console.WriteLine($"\n({items.Count} items)");
}
