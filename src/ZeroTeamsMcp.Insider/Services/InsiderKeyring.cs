using System.Security.Cryptography;
using System.Text;

namespace ZeroTeamsMcp.Insider.Services;

/// <summary>
/// Claves de acceso del MCP de investigación, una por persona.
///
/// Se separa del sistema de API keys del ControlPlane a propósito: las claves
/// del MCP público no deben abrir el módulo de investigación. Cada clave lleva
/// un nombre que acaba en la auditoría, de modo que se puede revocar a una
/// persona sin rotar las demás.
///
/// Formato de <c>INSIDER_KEYS</c>: <c>miguel=clave1,legal=clave2</c>.
/// Fallback de una sola clave: <c>INSIDER_API_KEY</c> + <c>INSIDER_ACTOR</c>.
/// </summary>
public sealed class InsiderKeyring
{
    private readonly List<(string Name, string Key)> _entries = [];

    public InsiderKeyring()
    {
        var raw = Environment.GetEnvironmentVariable("INSIDER_KEYS");

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sep = pair.IndexOf('=');
                if (sep <= 0 || sep == pair.Length - 1)
                    continue;

                var name = pair[..sep].Trim();
                var key = pair[(sep + 1)..].Trim();
                if (name.Length > 0 && key.Length >= 16)
                    _entries.Add((name, key));
            }
        }

        if (_entries.Count == 0)
        {
            var single = Environment.GetEnvironmentVariable("INSIDER_API_KEY");
            if (!string.IsNullOrWhiteSpace(single) && single.Trim().Length >= 16)
            {
                var actor = Environment.GetEnvironmentVariable("INSIDER_ACTOR") ?? "api-client";
                _entries.Add((actor.Trim(), single.Trim()));
            }
        }
    }

    public bool HasAny => _entries.Count > 0;

    public int Count => _entries.Count;

    public IReadOnlyList<string> ActorNames => _entries.Select(e => e.Name).ToList();

    /// <summary>Devuelve el nombre asociado a la clave, o null si no coincide con ninguna.</summary>
    public string? Resolve(string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided))
            return null;

        var candidate = Encoding.UTF8.GetBytes(provided.Trim());

        string? match = null;
        foreach (var (name, key) in _entries)
        {
            // Se compara contra todas las entradas siempre (sin cortocircuito)
            // para no filtrar por tiempo qué clave existe.
            var known = Encoding.UTF8.GetBytes(key);
            if (CryptographicOperations.FixedTimeEquals(candidate, known))
                match = name;
        }

        return match;
    }
}
