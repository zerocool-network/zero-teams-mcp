using System.Security.Cryptography;
using System.Text;

namespace ZeroTeamsMcp.ControlPlane.Services;

/// <summary>
/// Cifrado AES-256-CBC con clave derivada de ENCRYPTION_KEY para datos que
/// deben recuperarse en texto plano (API keys del panel). Formato: v1:base64(salt|iv|ct).
/// </summary>
public sealed class SecretCipher
{
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const byte Version = 1;

    private readonly byte[] _master;

    public SecretCipher(string? masterKey = null)
    {
        var raw = masterKey ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("ENCRYPTION_KEY environment variable is required but not set");
        _master = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

    public string Encrypt(string plainText)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var aes = Aes.Create();
        aes.Key = Derive(salt);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

        var buf = new byte[SaltSize + IvSize + cipher.Length];
        Buffer.BlockCopy(salt, 0, buf, 0, SaltSize);
        Buffer.BlockCopy(aes.IV, 0, buf, SaltSize, IvSize);
        Buffer.BlockCopy(cipher, 0, buf, SaltSize + IvSize, cipher.Length);
        return $"v{Version}:" + Convert.ToBase64String(buf);
    }

    /// <summary>Devuelve null ante cualquier fallo (datos corruptos o sin cifrar).</summary>
    public string? Decrypt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (!value.StartsWith($"v{Version}:", StringComparison.Ordinal)) return null;
            var buf = Convert.FromBase64String(value[(Version.ToString().Length + 2)..]);
            if (buf.Length < SaltSize + IvSize + 16) return null;

            var salt = buf[..SaltSize];
            var iv = buf[SaltSize..(SaltSize + IvSize)];
            var cipher = buf[(SaltSize + IvSize)..];

            using var aes = Aes.Create();
            aes.Key = Derive(salt);
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private byte[] Derive(byte[] salt) => SHA256.HashData([.. _master, .. salt]);
}
