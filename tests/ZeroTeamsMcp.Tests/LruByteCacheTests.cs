using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

public class LruByteCacheTests
{
    private static TeamsFileTools.CachedFile F(int size) =>
        new("text/plain", new byte[size]);

    [Fact]
    public void Store_DentroPresupuesto_MantieneTodasLasEntradas()
    {
        var cache = new LruByteCache(100);
        cache.Store("a", F(40));
        cache.Store("b", F(50));

        Assert.Equal(2, cache.Count);
        Assert.Equal(90, cache.EstimatedBytes);
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
    }

    [Fact]
    public void Store_AlExcederPresupuesto_EvictaLasMenosRecientes()
    {
        var cache = new LruByteCache(100);
        cache.Store("a", F(40));
        cache.Store("b", F(50));
        cache.Store("c", F(80));

        Assert.Equal(1, cache.Count);
        Assert.Equal(80, cache.EstimatedBytes);
        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void TryGet_ActualizaRecencia()
    {
        var cache = new LruByteCache(100);
        cache.Store("a", F(40));
        cache.Store("b", F(50));
        Assert.True(cache.TryGet("a", out _)); // a pasa a ser la más reciente

        cache.Store("c", F(30)); // fuerza evicción → debe caer b (la LMU)

        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Store_EntradaMayorQuePresupuesto_NoAlmacena()
    {
        var cache = new LruByteCache(100);
        cache.Store("x", F(150));

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("x", out _));
    }

    [Fact]
    public void Store_ReemplazaClaveYReajustaBytes()
    {
        var cache = new LruByteCache(100);
        cache.Store("a", F(40));
        cache.Store("a", F(30));

        Assert.Equal(1, cache.Count);
        Assert.Equal(30, cache.EstimatedBytes);
    }

    [Fact]
    public void TryGet_ClaveAusente_DevuelveFalso()
    {
        var cache = new LruByteCache(100);
        cache.Store("a", F(10));

        Assert.False(cache.TryGet("nope", out var file));
        Assert.Null(file);
    }
}