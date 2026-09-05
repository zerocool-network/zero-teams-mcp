using ZeroTeamsMcp.ControlPlane.Services;
using ZeroTeamsMcp.Core.Models;

namespace ZeroTeamsMcp.Tests;

public class TenantStoreTests : IDisposable
{
    private readonly TenantStore _store;
    private readonly string _tempDir;

    public TenantStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new TenantStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Add_NewTenant_ReturnsTrue()
    {
        var result = _store.Add(new TenantConfig
        {
            TenantId = "t-1",
            ClientId = "c-1",
            ClientSecret = "s-1",
            DisplayName = "Test"
        });
        Assert.True(result);
    }

    [Fact]
    public void Add_DuplicateTenant_ReturnsFalse()
    {
        var config = new TenantConfig
        {
            TenantId = "t-1",
            ClientId = "c-1",
            ClientSecret = "s-1",
            DisplayName = "Test"
        };
        _store.Add(config);
        var result = _store.Add(config);
        Assert.False(result);
    }

    [Fact]
    public void GetAll_Empty_ReturnsEmpty()
    {
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void GetAll_WithTenants_ReturnsAll()
    {
        _store.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });
        _store.Add(new TenantConfig { TenantId = "t-2", ClientId = "c-2", ClientSecret = "s-2", DisplayName = "B" });
        Assert.Equal(2, _store.GetAll().Count);
    }

    [Fact]
    public void GetById_Existing_ReturnsTenant()
    {
        _store.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });
        var found = _store.GetById("t-1");
        Assert.NotNull(found);
        Assert.Equal("A", found.DisplayName);
    }

    [Fact]
    public void GetById_NonExisting_ReturnsNull()
    {
        Assert.Null(_store.GetById("nonexistent"));
    }

    [Fact]
    public void Remove_Existing_ReturnsTrue()
    {
        _store.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });
        Assert.True(_store.Remove("t-1"));
        Assert.Null(_store.GetById("t-1"));
    }

    [Fact]
    public void Remove_NonExisting_ReturnsFalse()
    {
        Assert.False(_store.Remove("nonexistent"));
    }

    [Fact]
    public void UpdateConsent_NewRecord_ReturnsTrue()
    {
        _store.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });
        var result = _store.UpdateConsent("t-1", ConsentStatus.Granted);
        Assert.True(result);
        var consent = _store.GetConsent("t-1");
        Assert.NotNull(consent);
        Assert.Equal(ConsentStatus.Granted, consent.Status);
    }

    [Fact]
    public void UpdateConsent_NonExistingTenant_ReturnsFalse()
    {
        Assert.False(_store.UpdateConsent("nonexistent", ConsentStatus.Granted));
    }

    [Fact]
    public void GetConsent_AfterAdd_ReturnsPending()
    {
        _store.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });
        var consent = _store.GetConsent("t-1");
        Assert.NotNull(consent);
        Assert.Equal(ConsentStatus.Pending, consent.Status);
    }

    [Fact]
    public void Persistence_WritesAndReads()
    {
        var store1 = new TenantStore(_tempDir);
        store1.Add(new TenantConfig { TenantId = "t-1", ClientId = "c-1", ClientSecret = "s-1", DisplayName = "A" });

        var store2 = new TenantStore(_tempDir);
        var tenants = store2.GetAll();
        Assert.Single(tenants);
        Assert.Equal("A", tenants[0].DisplayName);
    }
}
