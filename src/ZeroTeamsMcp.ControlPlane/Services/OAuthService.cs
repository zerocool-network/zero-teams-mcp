using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed class OAuthService
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly string _redirectUri;

    private static readonly string[] DefaultScopes =
    [
        "openid", "profile", "email",
        "offline_access",
        "User.Read", "User.ReadBasic.All",
        "Team.ReadBasic.All", "Channel.ReadBasic.All",
        "Chat.Read", "Chat.ReadBasic",
        "ChatMessage.Send", "Chat.Create",
        "ChannelMessage.Send"
    ];

    public OAuthService(HttpClient http)
    {
        _http = http;
        _clientId = Environment.GetEnvironmentVariable("OAUTH_CLIENT_ID") ?? "";
        _clientSecret = Environment.GetEnvironmentVariable("OAUTH_CLIENT_SECRET") ?? "";
        _tenantId = Environment.GetEnvironmentVariable("OAUTH_TENANT_ID") ?? "";
        _redirectUri = Environment.GetEnvironmentVariable("OAUTH_REDIRECT_URI") ?? "http://localhost:5100/auth/callback";
    }

    public string ClientId => _clientId;
    public string TenantId => _tenantId;

    public string GenerateCodeVerifier()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public string GenerateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public string GetAuthorizationUrl(string codeChallenge, string state)
    {
        var scopes = string.Join(" ", DefaultScopes);
        return $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/authorize" +
               $"?client_id={_clientId}" +
               $"&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
               $"&scope={Uri.EscapeDataString(scopes)}" +
               $"&response_mode=query" +
               $"&code_challenge={codeChallenge}" +
               $"&code_challenge_method=S256" +
               $"&state={state}";
    }

    public async Task<UserToken?> ExchangeCodeAsync(string code, string codeVerifier)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = string.Join(" ", DefaultScopes)
        };

        var response = await _http.PostAsync(
            $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(body));

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (json?.AccessToken is null) return null;

        var userInfo = await GetUserInfoAsync(json.AccessToken);

        return new UserToken
        {
            UserId = userInfo?.Id ?? "",
            Email = userInfo?.Mail ?? userInfo?.UserPrincipalName ?? "",
            AccessToken = json.AccessToken,
            RefreshToken = json.RefreshToken ?? "",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(json.ExpiresIn),
            Scopes = string.Join(" ", DefaultScopes)
        };
    }

    public async Task<UserToken?> RefreshTokenAsync(string refreshToken)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = string.Join(" ", DefaultScopes)
        };

        var response = await _http.PostAsync(
            $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(body));

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (json?.AccessToken is null) return null;

        return new UserToken
        {
            AccessToken = json.AccessToken,
            RefreshToken = json.RefreshToken ?? refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(json.ExpiresIn),
            Scopes = string.Join(" ", DefaultScopes)
        };
    }

    public async Task<UserInfo?> GetUserInfoAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<UserInfo>();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    public sealed class UserInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("mail")]
        public string? Mail { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}
