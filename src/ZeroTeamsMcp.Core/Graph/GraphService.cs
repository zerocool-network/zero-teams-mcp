using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using System.Collections.Concurrent;
using ZeroTeamsMcp.Core.Auth;

namespace ZeroTeamsMcp.Core.Graph;

public sealed class GraphService
{
    private readonly GraphAuthProvider _authProvider;
    private readonly DelegatedAuthProvider? _delegatedProvider;
    private readonly ConcurrentDictionary<string, CachedClient> _clientCache = new();
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(50);

    public GraphService(GraphAuthProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public GraphService(
        GraphAuthProvider authProvider,
        DelegatedAuthProvider delegatedProvider)
    {
        _authProvider = authProvider;
        _delegatedProvider = delegatedProvider;
    }

    public async Task<GraphServiceClient> GetClientAsync(
        string? userId = null,
        CancellationToken ct = default)
    {
        var cacheKey = userId ?? "app-only";

        if (_clientCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTimeOffset.UtcNow - cached.CreatedAt < _cacheTtl)
            {
                return cached.Client;
            }

            // Token cacheado vencido (o cerca de vencer): invalidar y forzar recreación.
            if (_clientCache.TryRemove(cacheKey, out var stale))
            {
                stale.Client.Dispose();
            }
        }

        GraphServiceClient client;

        if (!string.IsNullOrEmpty(userId) && _delegatedProvider is not null)
        {
            try
            {
                var ctx = new Dictionary<string, object> { ["userId"] = userId };
                var token = await _delegatedProvider.GetAuthorizationTokenAsync(
                    new Uri("https://graph.microsoft.com"), ctx, ct);
                var provider = new BaseBearerTokenAuthenticationProvider(
                    new StaticTokenProvider(token));
                client = new GraphServiceClient(provider);
            }
            catch
            {
                client = await CreateAppOnlyClientAsync(ct);
            }
        }
        else
        {
            client = await CreateAppOnlyClientAsync(ct);
        }

        _clientCache[cacheKey] = new CachedClient(client, DateTimeOffset.UtcNow);
        return client;
    }

    private async Task<GraphServiceClient> CreateAppOnlyClientAsync(CancellationToken ct)
    {
        var appToken = await _authProvider.GetAccessTokenAsync(ct);
        var appProvider = new BaseBearerTokenAuthenticationProvider(
            new StaticTokenProvider(appToken));
        return new GraphServiceClient(appProvider);
    }

    public void InvalidateCache(string? userId = null)
    {
        var key = userId ?? "app-only";
        if (_clientCache.TryRemove(key, out var cached))
        {
            cached.Client.Dispose();
        }
    }

    public void InvalidateAll()
    {
        foreach (var kvp in _clientCache)
        {
            kvp.Value.Client.Dispose();
        }
        _clientCache.Clear();
    }

    private sealed record CachedClient(GraphServiceClient Client, DateTimeOffset CreatedAt);
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