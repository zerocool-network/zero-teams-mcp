using System.Data;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using ZeroTeamsMcp.Core.Models;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed class TenantStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private readonly RedisLockService? _redisLock;

    private readonly string _dataDir;

    public TenantStore(string? dataDir = null, RedisLockService? redisLock = null)
    {
        _redisLock = redisLock;
        _dataDir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_dataDir);
        var dbPath = Path.Combine(_dataDir, "controlplane.db");
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initLock.Wait();
        try
        {
            if (_initialized) return;
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                
                CREATE TABLE IF NOT EXISTS tenants (
                    tenant_id TEXT PRIMARY KEY,
                    client_id TEXT NOT NULL,
                    client_secret TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                
                CREATE TABLE IF NOT EXISTS consents (
                    tenant_id TEXT PRIMARY KEY,
                    status INTEGER NOT NULL,
                    granted_at TEXT,
                    revoked_at TEXT,
                    error_message TEXT,
                    FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
                );
                
                CREATE INDEX IF NOT EXISTS idx_consents_status ON consents(status);
            ";
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private async Task<IDisposable?> AcquireTenantLockAsync(string tenantId, CancellationToken ct = default)
    {
        if (_redisLock is null) return null;
        return await _redisLock.LockAsync($"tenant:{tenantId}", TimeSpan.FromSeconds(30), ct);
    }

    public List<TenantConfig> GetAll()
    {
        EnsureInitialized();
        var tenants = new List<TenantConfig>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tenant_id, client_id, client_secret, display_name, created_at FROM tenants";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tenants.Add(new TenantConfig
            {
                TenantId = reader.GetString(0),
                ClientId = reader.GetString(1),
                ClientSecret = reader.GetString(2),
                DisplayName = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }
        return tenants;
    }

    public TenantConfig? GetById(string tenantId)
    {
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tenant_id, client_id, client_secret, display_name, created_at FROM tenants WHERE tenant_id = @id";
        cmd.Parameters.AddWithValue("@id", tenantId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new TenantConfig
        {
            TenantId = reader.GetString(0),
            ClientId = reader.GetString(1),
            ClientSecret = reader.GetString(2),
            DisplayName = reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4))
        };
    }

    public async Task<bool> AddAsync(TenantConfig config, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var lockHandle = await AcquireTenantLockAsync(config.TenantId, ct);
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO tenants (tenant_id, client_id, client_secret, display_name, created_at) VALUES (@tid, @cid, @cs, @dn, @ca)";
            cmd.Parameters.AddWithValue("@tid", config.TenantId);
            cmd.Parameters.AddWithValue("@cid", config.ClientId);
            cmd.Parameters.AddWithValue("@cs", config.ClientSecret);
            cmd.Parameters.AddWithValue("@dn", config.DisplayName);
            cmd.Parameters.AddWithValue("@ca", config.CreatedAt.ToString("O"));
            var rows = cmd.ExecuteNonQuery();
            if (rows == 0) return false;

            using var cmd2 = conn.CreateCommand();
            cmd2.Transaction = tx;
            cmd2.CommandText = "INSERT INTO consents (tenant_id, status) VALUES (@tid, 0)";
            cmd2.Parameters.AddWithValue("@tid", config.TenantId);
            cmd2.ExecuteNonQuery();

            tx.Commit();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public bool Add(TenantConfig config)
    {
        return AddAsync(config).GetAwaiter().GetResult();
    }

    public async Task<bool> RemoveAsync(string tenantId, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var lockHandle = await AcquireTenantLockAsync(tenantId, ct);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tenants WHERE tenant_id = @id";
        cmd.Parameters.AddWithValue("@id", tenantId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Remove(string tenantId)
    {
        return RemoveAsync(tenantId).GetAwaiter().GetResult();
    }

    public ConsentRecord? GetConsent(string tenantId)
    {
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tenant_id, status, granted_at, revoked_at, error_message FROM consents WHERE tenant_id = @id";
        cmd.Parameters.AddWithValue("@id", tenantId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ConsentRecord
        {
            TenantId = reader.GetString(0),
            Status = (ConsentStatus)reader.GetInt32(1),
            GrantedAt = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
            RevokedAt = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
            ErrorMessage = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    public async Task<bool> UpdateConsentAsync(string tenantId, ConsentStatus status, string? error = null, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var lockHandle = await AcquireTenantLockAsync(tenantId, ct);
        using var conn = CreateConnection();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM tenants WHERE tenant_id = @id";
        checkCmd.Parameters.AddWithValue("@id", tenantId);
        if (checkCmd.ExecuteScalar() is null) return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO consents (tenant_id, status, granted_at, revoked_at, error_message)
            VALUES (@tid, @st, @ga, @ra, @em)
            ON CONFLICT(tenant_id) DO UPDATE SET
                status = @st,
                granted_at = COALESCE(@ga, granted_at),
                revoked_at = COALESCE(@ra, revoked_at),
                error_message = @em";
        cmd.Parameters.AddWithValue("@tid", tenantId);
        cmd.Parameters.AddWithValue("@st", (int)status);
        cmd.Parameters.AddWithValue("@ga", status == ConsentStatus.Granted ? DateTime.UtcNow.ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ra", status == ConsentStatus.Revoked ? DateTime.UtcNow.ToString("O") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@em", error ?? (object)DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdateConsent(string tenantId, ConsentStatus status, string? error = null)
    {
        return UpdateConsentAsync(tenantId, status, error).GetAwaiter().GetResult();
    }
}