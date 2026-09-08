using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed class UserToken
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("scopes")]
    public string Scopes { get; set; } = "";
}

public sealed class TokenStore
{
    private readonly string _connectionString;
    private readonly byte[] _masterKey;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private readonly OAuthService _oauth;
    private readonly RedisLockService? _redisLock;

    private const int SaltSize = 32;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private const int IvSize = 16;
    private const int Version = 2;

    public TokenStore(string? dataDir, OAuthService oauth, RedisLockService? redisLock = null)
    {
        _oauth = oauth;
        _redisLock = redisLock;
        _dataDir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_dataDir);
        var dbPath = Path.Combine(_dataDir, "controlplane.db");
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";

        var encKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(encKey))
        {
            throw new InvalidOperationException("ENCRYPTION_KEY environment variable is required but not set");
        }
        if (encKey.Length < 32)
        {
            throw new InvalidOperationException("ENCRYPTION_KEY must be at least 32 characters. Generate one with: openssl rand -base64 32");
        }
        _masterKey = DeriveKey(encKey, [.. new byte[SaltSize]]);

        EnsureInitialized();
    }

    private readonly string _dataDir;

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
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
                
                CREATE TABLE IF NOT EXISTS user_tokens (
                    user_id TEXT PRIMARY KEY,
                    email TEXT NOT NULL,
                    encrypted_token BLOB NOT NULL,
                    updated_at TEXT NOT NULL
                );
                
                CREATE INDEX IF NOT EXISTS idx_user_tokens_email ON user_tokens(email);
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

    private async Task<IDisposable?> AcquireTokenLockAsync(string userId, CancellationToken ct = default)
    {
        if (_redisLock is null) return null;
        return await _redisLock.LockAsync($"token:{userId}", TimeSpan.FromSeconds(30), ct);
    }

    private byte[] Encrypt(string plainText)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(Encoding.UTF8.GetString(_masterKey), salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[1 + SaltSize + IvSize + cipherBytes.Length];
        result[0] = Version;
        Buffer.BlockCopy(salt, 0, result, 1, SaltSize);
        Buffer.BlockCopy(aes.IV, 0, result, 1 + SaltSize, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, result, 1 + SaltSize + IvSize, cipherBytes.Length);
        return result;
    }

    private string Decrypt(byte[] cipherData)
    {
        if (cipherData.Length < 1 + SaltSize + IvSize)
            throw new CryptographicException("Invalid ciphertext");

        var version = cipherData[0];
        if (version != Version)
        {
            return DecryptLegacy(cipherData);
        }

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        Buffer.BlockCopy(cipherData, 1, salt, 0, SaltSize);
        Buffer.BlockCopy(cipherData, 1 + SaltSize, iv, 0, IvSize);
        var cipher = new byte[cipherData.Length - 1 - SaltSize - IvSize];
        Buffer.BlockCopy(cipherData, 1 + SaltSize + IvSize, cipher, 0, cipher.Length);

        var key = DeriveKey(Encoding.UTF8.GetString(_masterKey), salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private string DecryptLegacy(byte[] cipherData)
    {
        var legacyKey = Encoding.UTF8.GetBytes(
            (Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "").PadRight(32, '\0')[..32]);

        using var aes = Aes.Create();
        aes.Key = legacyKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = new byte[aes.IV.Length];
        var cipher = new byte[cipherData.Length - iv.Length];
        Buffer.BlockCopy(cipherData, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(cipherData, iv.Length, cipher, 0, cipher.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public async Task<UserToken?> GetTokenAsync(string userId)
    {
        EnsureInitialized();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, email, encrypted_token FROM user_tokens WHERE user_id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var email = reader.GetString(1);
        var encrypted = (byte[])reader.GetValue(2);
        var json = Decrypt(encrypted);
        var token = JsonSerializer.Deserialize<UserToken>(json);
        if (token is null) return null;
        token.Email = email;

        if (token.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(5) && !string.IsNullOrEmpty(token.RefreshToken))
        {
            var refreshed = await _oauth.RefreshTokenAsync(token.RefreshToken);
            if (refreshed is not null)
            {
                token.AccessToken = refreshed.AccessToken;
                token.ExpiresAt = refreshed.ExpiresAt;
                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    token.RefreshToken = refreshed.RefreshToken;
                await SaveTokenAsync(token);
            }
        }
        return token;
    }

    public async Task SaveTokenAsync(UserToken token, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var lockHandle = await AcquireTokenLockAsync(token.UserId, ct);
        var encrypted = Encrypt(JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO user_tokens (user_id, email, encrypted_token, updated_at)
            VALUES (@uid, @em, @et, @ua)
            ON CONFLICT(user_id) DO UPDATE SET
                email = @em,
                encrypted_token = @et,
                updated_at = @ua";
        cmd.Parameters.AddWithValue("@uid", token.UserId);
        cmd.Parameters.AddWithValue("@em", token.Email);
        cmd.Parameters.AddWithValue("@et", encrypted);
        cmd.Parameters.AddWithValue("@ua", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SaveToken(UserToken token)
    {
        SaveTokenAsync(token).GetAwaiter().GetResult();
    }

    public async Task<bool> RevokeTokenAsync(string userId, CancellationToken ct = default)
    {
        EnsureInitialized();
        using var lockHandle = await AcquireTokenLockAsync(userId, ct);
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_tokens WHERE user_id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool RevokeToken(string userId)
    {
        return RevokeTokenAsync(userId).GetAwaiter().GetResult();
    }

    public List<UserToken> GetAllTokens()
    {
        EnsureInitialized();
        var tokens = new List<UserToken>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, email, encrypted_token FROM user_tokens";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var email = reader.GetString(1);
            var encrypted = (byte[])reader.GetValue(2);
            var json = Decrypt(encrypted);
            var token = JsonSerializer.Deserialize<UserToken>(json);
            if (token is not null)
            {
                token.Email = email;
                tokens.Add(token);
            }
        }
        return tokens;
    }
}