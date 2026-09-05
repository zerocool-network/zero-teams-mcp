using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using ZeroTeamsMcp.Core.Auth;

namespace ZeroTeamsMcp.Core.Graph;

public sealed class GraphService
{
    private readonly GraphAuthProvider _authProvider;

    public GraphService(GraphAuthProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public async Task<GraphServiceClient> GetClientAsync(CancellationToken ct = default)
    {
        var token = await _authProvider.GetAccessTokenAsync(ct);
        var provider = new BaseBearerTokenAuthenticationProvider(new StaticTokenProvider(token));
        return new GraphServiceClient(provider);
    }
}

internal sealed class StaticTokenProvider(string token) : IAccessTokenProvider
{
    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(token);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; } = new();
}
