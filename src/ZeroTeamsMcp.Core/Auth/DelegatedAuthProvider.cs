using System.Net.Http.Json;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace ZeroTeamsMcp.Core.Auth;

public sealed class DelegatedAuthProvider : IAccessTokenProvider
{
    private readonly HttpClient _http;
    private readonly string _controlPlaneUrl;

    public DelegatedAuthProvider(HttpClient http, string? controlPlaneUrl = null)
    {
        _http = http;
        _controlPlaneUrl = controlPlaneUrl ?? "http://localhost:5100";
    }

    public AllowedHostsValidator AllowedHostsValidator { get; } = new();

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (additionalAuthenticationContext != null &&
            additionalAuthenticationContext.TryGetValue("userId", out var userIdObj) &&
            userIdObj is string userId)
        {
            try
            {
                var response = await _http.GetAsync(
                    $"{_controlPlaneUrl}/auth/token/{userId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadFromJsonAsync<TokenInfo>(
                        cancellationToken);
                    if (json?.AccessToken is not null)
                        return json.AccessToken;
                }
            }
            catch
            {
                // Fall through
            }
        }

        throw new UnauthorizedAccessException(
            "No delegated token available. User must authenticate via OAuth.");
    }

    private sealed class TokenInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }
    }
}
