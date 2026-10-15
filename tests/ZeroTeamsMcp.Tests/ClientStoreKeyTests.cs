using ZeroTeamsMcp.ControlPlane.Services;

namespace ZeroTeamsMcp.Tests;

/// <summary>
/// La key del panel debe poder recuperarse (cifrada) para mostrarla en
/// "Mi conexión" y seguir validando por hash como siempre.
/// </summary>
public class ClientStoreKeyTests : IDisposable
{
    private readonly string _dir;
    private readonly SecretCipher _cipher = new("clave-de-prueba-con-longitud-suficiente");
    private readonly ClientStore _store;

    public ClientStoreKeyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zt-clients-" + Guid.NewGuid().ToString("N"));
        _store = new ClientStore(_dir, redisLock: null, cipher: _cipher);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Register_GuardaCopiaCifrada_YFindByEmail_LaDevuelve()
    {
        var (apiKey, prefix) = await _store.RegisterAsync("cliente@example.com", isAdmin: false);

        var found = await _store.FindByEmailAsync("cliente@example.com");

        Assert.NotNull(found);
        Assert.Equal("cliente@example.com", found!.Email);
        Assert.Equal(prefix, found.KeyPrefix);
        Assert.Equal(apiKey, found.ApiKey);
        Assert.False(found.IsAdmin);
    }

    [Fact]
    public async Task FindByEmail_NoDevuelveKeysRevocadas()
    {
        var (apiKey, _) = await _store.RegisterAsync("revocado@example.com", isAdmin: false);
        Assert.True(_store.Revoke(apiKey));

        Assert.Null(await _store.FindByEmailAsync("revocado@example.com"));
    }

    [Fact]
    public async Task FindByEmail_DevuelveLaMasReciente()
    {
        await _store.RegisterAsync("dos@example.com", isAdmin: false);
        var (segunda, _) = await _store.RegisterAsync("dos@example.com", isAdmin: false);

        var found = await _store.FindByEmailAsync("dos@example.com");

        Assert.NotNull(found);
        Assert.Equal(segunda, found!.ApiKey);
    }

    [Fact]
    public async Task FindByEmail_EmailDesconocido_DevuelveNull()
    {
        Assert.Null(await _store.FindByEmailAsync("nadie@example.com"));
        Assert.Null(await _store.FindByEmailAsync(""));
    }

    [Fact]
    public async Task List_NoExponeLaKey()
    {
        await _store.RegisterAsync("lista@example.com", isAdmin: false);

        var client = Assert.Single(_store.List(), c => c.Email == "lista@example.com");
        Assert.Null(client.ApiKey);
    }

    [Fact]
    public async Task Validate_SigueFuncionandoConCifradoActivo()
    {
        var (apiKey, _) = await _store.RegisterAsync("valida@example.com", isAdmin: false);

        var client = await _store.ValidateAsync(apiKey);

        Assert.NotNull(client);
        Assert.Equal("valida@example.com", client!.Email);
    }

    [Fact]
    public async Task SinCipher_LaKeyNoSePuedeRecuperar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zt-clients-nocipher-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientStore(dir);
            await store.RegisterAsync("sin-cipher@example.com", isAdmin: false);

            var found = await store.FindByEmailAsync("sin-cipher@example.com");

            Assert.NotNull(found);
            Assert.Null(found!.ApiKey);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Cipher_Roundtrip_YRechazaBasura()
    {
        var original = "zk_clave-secreta_1234567890";
        var enc = _cipher.Encrypt(original);

        Assert.Equal(original, _cipher.Decrypt(enc));
        Assert.Null(_cipher.Decrypt("v1:no-es-base64!!"));
        Assert.Null(_cipher.Decrypt(null));
        Assert.Null(_cipher.Decrypt(""));
        Assert.Null(_cipher.Decrypt(enc[..^4]));
        Assert.NotEqual(enc, _cipher.Encrypt(original)); // IV/salt nuevos cada vez
    }

    [Fact]
    public void Cipher_ClaveDistinta_NoDescifra()
    {
        var enc = _cipher.Encrypt("secreto");
        var otro = new SecretCipher("otra-clave-de-prueba-suficientemente-larga");

        Assert.Null(otro.Decrypt(enc));
    }
}
