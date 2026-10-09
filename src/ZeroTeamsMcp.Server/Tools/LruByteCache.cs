namespace ZeroTeamsMcp.Server.Tools;

/// <summary>
/// Caché LRU de ficheros ya descargados, acotada en bytes. Al insertar por encima del presupuesto
/// evicta la entrada menos recientemente usada. Reloj propio monótono (evita empates de recencia
/// a resolución de milisegundo) y acceso blindado con lock.
/// </summary>
public sealed class LruByteCache
{
    private sealed record Entry(TeamsFileTools.CachedFile File, long LastAccess);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly long _budgetBytes;
    private long _bytes;
    private long _clock;

    public LruByteCache(long budgetBytes)
    {
        _budgetBytes = budgetBytes;
    }

    /// <summary>Número de entradas actuales.</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>Bytes totales cached (suma de los ficheros, no del overhead).</summary>
    public long EstimatedBytes
    {
        get { lock (_gate) return _bytes; }
    }

    /// <summary>Recupera la entrada y la marca como más recientemente usada.</summary>
    public bool TryGet(string key, out TeamsFileTools.CachedFile? file)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                var hit = entry with { LastAccess = NextClockLocked() };
                _entries[key] = hit;
                file = entry.File;
                return true;
            }
            file = null;
            return false;
        }
    }

    /// <summary>Inserta o reemplaza. Si la entrada sola supera el presupuesto, no se almacena.</summary>
    public void Store(string key, TeamsFileTools.CachedFile file)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _bytes -= existing.File.Bytes.Length;
                _entries.Remove(key);
            }
            if (!CanFitLocked(file.Bytes.Length)) return;
            _entries[key] = new Entry(file, NextClockLocked());
            _bytes += file.Bytes.Length;
        }
    }

    /// <summary>Devuelve true si <paramref name="additional"/> cabe, evictando LRU mientras haga falta.</summary>
    private bool CanFitLocked(long additional)
    {
        while (_entries.Count > 0 && _bytes + additional > _budgetBytes)
        {
            long minTick = long.MaxValue;
            string? victim = null;
            foreach (var (key, entry) in _entries)
            {
                if (entry.LastAccess < minTick)
                {
                    minTick = entry.LastAccess;
                    victim = key;
                }
            }
            if (victim is null) break;
            _bytes -= _entries[victim].File.Bytes.Length;
            _entries.Remove(victim);
        }
        return _bytes + additional <= _budgetBytes;
    }

    private long NextClockLocked() => ++_clock;
}