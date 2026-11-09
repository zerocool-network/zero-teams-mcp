using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ZeroTeamsMcp.ControlPlane.Services;

/// <summary>
/// Identidad de un cliente remoto registrado en el cloud.
/// </summary>
public sealed class RegisteredClient
{
    public string Email { get; init; } = "";
    public bool IsAdmin { get; init; }
    public string Role { get; init; } = ClientRoles.Client;
    public string KeyPrefix { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool Revoked { get; init; }
    public DateTime? LastUsedAt { get; init; }

    /// <summary>
    /// API key en texto plano. Solo se rellena en FindByEmailAsync (para que el
    /// usuario pueda ver su integración en el panel); en List() queda null.
    /// </summary>
    public string? ApiKey { get; init; }
}

/// <summary>
/// Registro de clientes remotos (API keys) para el cloud. La clave se almacena
/// como hash SHA-256; el texto plano solo se devuelve una vez en el registro.
/// </summary>
public sealed class ClientStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private readonly string _dataDir;
    private readonly RedisLockService? _redisLock;
    private readonly SecretCipher? _cipher;

    public ClientStore(string? dataDir = null, RedisLockService? redisLock = null, SecretCipher? cipher = null)
    {
        _redisLock = redisLock;
        _cipher = cipher;
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

                CREATE TABLE IF NOT EXISTS clients (
                    key_hash TEXT PRIMARY KEY,
                    key_prefix TEXT NOT NULL,
                    email TEXT NOT NULL,
                    is_admin INTEGER NOT NULL,
                    revoked INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    last_used_at TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_clients_email ON clients(email);
                CREATE INDEX IF NOT EXISTS idx_clients_revoked ON clients(revoked);
            ";
            cmd.ExecuteNonQuery();

            // Migración: copia cifrada de la key para poder mostrarla en el panel.
            using (var alt = conn.CreateCommand())
            {
                alt.CommandText = "ALTER TABLE clients ADD COLUMN key_encrypted TEXT";
                try { alt.ExecuteNonQuery(); }
                catch (Microsoft.Data.Sqlite.SqliteException) { /* columna ya existe */ }
            }
            using (var roleCol = conn.CreateCommand())
            {
                roleCol.CommandText = "ALTER TABLE clients ADD COLUMN role TEXT";
                try { roleCol.ExecuteNonQuery(); }
                catch (Microsoft.Data.Sqlite.SqliteException) { /* columna ya existe */ }
            }
            using (var backfill = conn.CreateCommand())
            {
                backfill.CommandText = @"
                    UPDATE clients
                    SET role = CASE WHEN is_admin = 1 THEN 'admin' ELSE 'client' END
                    WHERE role IS NULL OR role = ''";
                backfill.ExecuteNonQuery();
            }
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

    private async Task<IDisposable?> AcquireLockAsync(string key, CancellationToken ct = default)
    {
        if (_redisLock is null) return null;
        return await _redisLock.LockAsync($"client:{key}", TimeSpan.FromSeconds(30), ct);
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "zk_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Registra un cliente y devuelve la API key en texto plano (solo esta vez).
    /// </summary>
    public Task<(string ApiKey, string KeyPrefix)> RegisterAsync(
        string email,
        bool isAdmin,
        CancellationToken ct = default)
        => RegisterWithRoleAsync(email, isAdmin ? ClientRoles.Admin : ClientRoles.Client, ct);

    /// <summary>
    /// Registra admin (<c>*</c>), cliente (su email) o restricted (centinela
    /// <see cref="ClientRoles.RestrictedEmail"/>). La key en claro sale una sola vez.
    /// </summary>
    public async Task<(string ApiKey, string KeyPrefix)> RegisterWithRoleAsync(
        string email,
        string role,
        CancellationToken ct = default)
    {
        EnsureInitialized();
        var normalized = role.Trim().ToLowerInvariant();
        var isAdmin = ClientRoles.IsAdmin(normalized);
        var storedEmail = normalized switch
        {
            ClientRoles.Admin => "*",
            ClientRoles.Restricted => ClientRoles.RestrictedEmail,
            _ => email.Trim().ToLowerInvariant()
        };
        var key = GenerateKey();
        var hash = HashKey(key);
        var prefix = key[..10];
        using var lockHandle = await AcquireLockAsync(hash, ct);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO clients (key_hash, key_prefix, email, is_admin, revoked, created_at, key_encrypted, role)
            VALUES (@h, @p, @e, @a, 0, @c, @k, @r)";
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@p", prefix);
        cmd.Parameters.AddWithValue("@e", storedEmail);
        cmd.Parameters.AddWithValue("@a", isAdmin ? 1 : 0);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@k", (object?)_cipher?.Encrypt(key) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", isAdmin ? ClientRoles.Admin : normalized == ClientRoles.Restricted ? ClientRoles.Restricted : ClientRoles.Client);
        cmd.ExecuteNonQuery();
        return (key, prefix);
    }

    /// <summary>
    /// Busca la key activa de un email (la más reciente). ApiKey viene descifrada
    /// si había copia cifrada; null si la key es anterior a esta función.
    /// </summary>
    public async Task<RegisteredClient?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT key_prefix, email, is_admin, revoked, created_at, last_used_at, key_encrypted, role
            FROM clients
            WHERE email = @e AND revoked = 0
            ORDER BY created_at DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("@e", email);
        using var reader = cmd.ExecuteReader();
        if (!await reader.ReadAsync(ct)) return null;
        return new RegisteredClient
        {
            KeyPrefix = reader.GetString(0),
            Email = reader.GetString(1),
            IsAdmin = ReadRole(reader, 7, reader.GetInt32(2) == 1) == ClientRoles.Admin,
            Role = ReadRole(reader, 7, reader.GetInt32(2) == 1),
            Revoked = reader.GetInt32(3) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            LastUsedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            ApiKey = _cipher?.Decrypt(reader.IsDBNull(6) ? null : reader.GetString(6))
        };
    }

    /// <summary>
    /// Valida una API key y devuelve la identidad si está activa. Actualiza last_used_at.
    /// </summary>
    public async Task<RegisteredClient?> ValidateAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        EnsureInitialized();
        var hash = HashKey(key);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT key_prefix, email, is_admin, revoked, created_at, last_used_at, role
            FROM clients WHERE key_hash = @h";
        cmd.Parameters.AddWithValue("@h", hash);
        RegisteredClient? client = null;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return null;
            client = new RegisteredClient
            {
                KeyPrefix = reader.GetString(0),
                Email = reader.GetString(1),
                IsAdmin = ReadRole(reader, 6, reader.GetInt32(2) == 1) == ClientRoles.Admin,
                Role = ReadRole(reader, 6, reader.GetInt32(2) == 1),
                Revoked = reader.GetInt32(3) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                LastUsedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5))
            };
        }

        if (client.Revoked) return null;

        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE clients SET last_used_at = @t WHERE key_hash = @h";
            upd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("@h", hash);
            upd.ExecuteNonQuery();
        }
        return client;
    }

    public List<RegisteredClient> List()
    {
        EnsureInitialized();
        var clients = new List<RegisteredClient>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT key_prefix, email, is_admin, revoked, created_at, last_used_at, role
            FROM clients ORDER BY created_at";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            clients.Add(new RegisteredClient
            {
                KeyPrefix = reader.GetString(0),
                Email = reader.GetString(1),
                IsAdmin = ReadRole(reader, 6, reader.GetInt32(2) == 1) == ClientRoles.Admin,
                Role = ReadRole(reader, 6, reader.GetInt32(2) == 1),
                Revoked = reader.GetInt32(3) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                LastUsedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5))
            });
        }
        return clients;
    }

    private static string ReadRole(SqliteDataReader reader, int ordinal, bool isAdminColumn)
    {
        if (reader.IsDBNull(ordinal))
            return isAdminColumn ? ClientRoles.Admin : ClientRoles.Client;
        var role = reader.GetString(ordinal);
        if (string.IsNullOrWhiteSpace(role))
            return isAdminColumn ? ClientRoles.Admin : ClientRoles.Client;
        return role.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Revoca una API key por su texto plano (o prefijo).
    /// </summary>
    public bool Revoke(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        // Busca por hash exacto o por prefijo.
        cmd.CommandText = @"
            UPDATE clients SET revoked = 1
            WHERE key_hash = @h OR key_prefix = @p";
        cmd.Parameters.AddWithValue("@h", HashKey(key));
        cmd.Parameters.AddWithValue("@p", key);
        return cmd.ExecuteNonQuery() > 0;
    }
}
