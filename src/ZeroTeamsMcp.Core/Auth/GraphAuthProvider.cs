using Microsoft.Identity.Client;

namespace ZeroTeamsMcp.Core.Auth;

public sealed class GraphAuthProvider
{
    private readonly IConfidentialClientApplication _app;
    private readonly string[] _scopes = ["https://graph.microsoft.com/.default"];

    public GraphAuthProvider(string tenantId, string clientId, string clientSecret)
    {
        _app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .Build();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var result = await _app.AcquireTokenForClient(_scopes).ExecuteAsync(ct);
        return result.AccessToken;
    }
}
