using System.Text.Json;
using ZeroTeamsMcp.Core.Models;

namespace ZeroTeamsMcp.ControlPlane.Services;

public sealed class TenantStore
{
    private readonly string _dataDir;
    private readonly string _tenantsFile;
    private readonly string _consentFile;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<TenantConfig> _tenants = [];
    private List<ConsentRecord> _consents = [];

    public TenantStore(string? dataDir = null)
    {
        _dataDir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_dataDir);
        _tenantsFile = Path.Combine(_dataDir, "tenants.json");
        _consentFile = Path.Combine(_dataDir, "consents.json");
        Load();
    }

    private void Load()
    {
        if (File.Exists(_tenantsFile))
            _tenants = JsonSerializer.Deserialize<List<TenantConfig>>(File.ReadAllText(_tenantsFile)) ?? [];
        if (File.Exists(_consentFile))
            _consents = JsonSerializer.Deserialize<List<ConsentRecord>>(File.ReadAllText(_consentFile)) ?? [];
    }

    private void Save()
    {
        File.WriteAllText(_tenantsFile, JsonSerializer.Serialize(_tenants, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(_consentFile, JsonSerializer.Serialize(_consents, new JsonSerializerOptions { WriteIndented = true }));
    }

    public List<TenantConfig> GetAll()
    {
        lock (_lock) return [.. _tenants];
    }

    public TenantConfig? GetById(string tenantId)
    {
        lock (_lock) return _tenants.FirstOrDefault(t => t.TenantId == tenantId);
    }

    public bool Add(TenantConfig config)
    {
        _lock.Wait();
        try
        {
            if (_tenants.Any(t => t.TenantId == config.TenantId)) return false;
            _tenants.Add(config);
            _consents.Add(new ConsentRecord { TenantId = config.TenantId });
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    public bool Remove(string tenantId)
    {
        _lock.Wait();
        try
        {
            var removed = _tenants.RemoveAll(t => t.TenantId == tenantId) > 0;
            if (removed) Save();
            return removed;
        }
        finally { _lock.Release(); }
    }

    public ConsentRecord? GetConsent(string tenantId)
    {
        lock (_lock) return _consents.FirstOrDefault(c => c.TenantId == tenantId);
    }

    public bool UpdateConsent(string tenantId, ConsentStatus status, string? error = null)
    {
        _lock.Wait();
        try
        {
            var record = _consents.FirstOrDefault(c => c.TenantId == tenantId);
            if (record is null) return false;
            record.Status = status;
            if (status == ConsentStatus.Granted) record.GrantedAt = DateTime.UtcNow;
            if (status == ConsentStatus.Revoked) record.RevokedAt = DateTime.UtcNow;
            if (error is not null) record.ErrorMessage = error;
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }
}
