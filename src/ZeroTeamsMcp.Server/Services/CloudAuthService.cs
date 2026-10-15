using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Resuelve una API key de cliente contra el ControlPlane, con caché en memoria.
/// </summary>
public sealed class CloudAuthService
{
    private readonly HttpClient _http;
    private readonly string _controlPlaneUrl;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (ClientIdentity? Identity, DateTimeOffset Expiry)> _cache = new();

    public CloudAuthService(IHttpClientFactory httpFactory, string? controlPlaneUrl = null)
    {
        _controlPlaneUrl = controlPlaneUrl ?? "http://localhost:5100";
        _http = httpFactory.CreateClient("controlplane");
    }

    public async Task<ClientIdentity?> ResolveAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        if (_cache.TryGetValue(apiKey, out var entry) && entry.Expiry > DateTimeOffset.UtcNow)
            return entry.Identity;

        ClientIdentity? identity = null;
        try
        {
            var url = $"{_controlPlaneUrl}/api/clients/validate?key={Uri.EscapeDataString(apiKey)}";
            var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ValidateResponse>(cancellationToken: ct);
                if (result is not null)
                {
                    identity = new ClientIdentity
                    {
                        Email = result.Email ?? "",
                        IsAdmin = result.IsAdmin
                    };
                }
            }
        }
        catch
        {
            // ControlPlane caído => no autenticar (fail closed).
            identity = null;
        }

        _cache[apiKey] = (identity, DateTimeOffset.UtcNow.Add(_cacheTtl));
        return identity;
    }

    private sealed class ValidateResponse
    {
        public string? Email { get; set; }
        public bool IsAdmin { get; set; }
        public string? KeyPrefix { get; set; }
    }
}
