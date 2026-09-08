using StackExchange.Redis;
using System.Diagnostics;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed class RedisLockService : IDisposable, IDistributedLockProvider
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly string _lockPrefix;
    private readonly TimeSpan _defaultExpiry;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxRetries;

    public RedisLockService(string connectionString, string lockPrefix = "zeroteams:lock:", TimeSpan? defaultExpiry = null)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
        _lockPrefix = lockPrefix;
        _defaultExpiry = defaultExpiry ?? TimeSpan.FromSeconds(30);
        _retryDelay = TimeSpan.FromMilliseconds(100);
        _maxRetries = 30;
    }

    public async Task<bool> AcquireLockAsync(string key, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var lockKey = _lockPrefix + key;
        var token = Guid.NewGuid().ToString("N");
        var lockExpiry = expiry ?? _defaultExpiry;

        for (int i = 0; i < _maxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();

            var acquired = await _db.StringSetAsync(lockKey, token, lockExpiry, When.NotExists);
            if (acquired)
            {
                return true;
            }

            await Task.Delay(_retryDelay, ct);
        }
        return false;
    }

    public async Task<bool> ReleaseLockAsync(string key, CancellationToken ct = default)
    {
        var lockKey = _lockPrefix + key;
        var script = LuaScript.Prepare(@"
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end");
        var result = await _db.ScriptEvaluateAsync(script, new { Key = lockKey, Value = "" });
        return (int)result > 0;
    }

    public async Task<IDisposable?> LockAsync(string key, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var acquired = await AcquireLockAsync(key, expiry, ct);
        if (!acquired) return null;

        return new RedisLock(this, key, ct);
    }

    public async Task<bool> TryLockAsync(string key, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        return await AcquireLockAsync(key, expiry, ct);
    }

    public async Task<bool> ExtendLockAsync(string key, TimeSpan expiry, CancellationToken ct = default)
    {
        var lockKey = _lockPrefix + key;
        var script = LuaScript.Prepare(@"
            if redis.call('GET', KEYS[1]) ~= false then
                return redis.call('EXPIRE', KEYS[1], ARGV[1])
            else
                return 0
            end");
        var result = await _db.ScriptEvaluateAsync(script, new { Key = lockKey, Expiry = (int)expiry.TotalSeconds });
        return (int)result > 0;
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }

    private sealed class RedisLock : IDisposable
    {
        private readonly RedisLockService _service;
        private readonly string _key;
        private readonly CancellationToken _ct;
        private bool _disposed;

        public RedisLock(RedisLockService service, string key, CancellationToken ct)
        {
            _service = service;
            _key = key;
            _ct = ct;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _ = _service.ReleaseLockAsync(_key, _ct);
            }
        }
    }
}

public interface IDistributedLockProvider
{
    Task<IDisposable?> LockAsync(string key, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> TryLockAsync(string key, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> ReleaseLockAsync(string key, CancellationToken ct = default);
}