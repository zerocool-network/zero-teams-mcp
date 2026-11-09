using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ZeroTeamsMcp.Insider.Services;

/// <summary>Scope autorizado de un caso de investigación.</summary>
public sealed record InsiderScope(bool Mail, bool Chat, bool Files, bool Signals)
{
    public static InsiderScope Full => new(true, true, true, true);

    public bool Allows(string kind) => kind switch
    {
        "mail" => Mail,
        "chat" => Chat,
        "files" => Files,
        "signals" => Signals,
        _ => false
    };
}

/// <summary>Caso de investigación aprobado por dos personas.</summary>
public sealed record InsiderCase(
    string CaseId,
    string Name,
    string Status,
    string Justification,
    string? LegalBasis,
    string RequestedBy,
    string ApprovedBy,
    IReadOnlyList<string> Custodians,
    InsiderScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt,
    string? ClosedReason)
{
    public bool IsOpen => Status == "open";

    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;

    public bool Contains(string custodian) =>
        Custodians.Any(c => string.Equals(c, custodian, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Entrada del log append-only encadenado por SHA-256.</summary>
public sealed record InsiderAuditEntry(
    long Seq,
    DateTimeOffset Timestamp,
    string? CaseId,
    string Actor,
    string Tool,
    string? Target,
    string? Query,
    int ResultCount,
    string Outcome,
    string? Detail,
    string PrevHash,
    string Hash);

/// <summary>
/// Almacén de casos de investigación + auditoría inmutable (hash-chain).
///
/// La auditoría vive aquí y no en ILogger porque el log de aplicación rota,
/// no es append-only y no permite demostrar que nadie lo editó. Cada entrada
/// incluye el hash de la anterior: alterar una fila rompe la cadena.
/// </summary>
public sealed class InsiderStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _connectionString;
    private readonly ILogger<InsiderStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public InsiderStore(ILogger<InsiderStore> logger)
    {
        _logger = logger;

        var path = Environment.GetEnvironmentVariable("INSIDER_DB_PATH") ?? "data/insider.db";
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        Initialize();
        _logger.LogInformation("InsiderStore initialized at {Path}", full);
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS insider_cases (
                case_id       TEXT PRIMARY KEY,
                name          TEXT NOT NULL,
                status        TEXT NOT NULL,
                justification TEXT NOT NULL,
                legal_basis   TEXT,
                requested_by  TEXT NOT NULL,
                approved_by   TEXT NOT NULL,
                custodians    TEXT NOT NULL,
                scope         TEXT NOT NULL,
                created_at    TEXT NOT NULL,
                expires_at    TEXT NOT NULL,
                closed_at     TEXT,
                closed_reason TEXT
            );

            CREATE TABLE IF NOT EXISTS insider_audit (
                seq          INTEGER PRIMARY KEY AUTOINCREMENT,
                ts           TEXT NOT NULL,
                case_id      TEXT,
                actor        TEXT NOT NULL,
                tool         TEXT NOT NULL,
                target       TEXT,
                query        TEXT,
                result_count INTEGER NOT NULL,
                outcome      TEXT NOT NULL,
                detail       TEXT,
                prev_hash    TEXT NOT NULL,
                hash         TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_audit_case ON insider_audit(case_id);
            CREATE INDEX IF NOT EXISTS idx_audit_ts ON insider_audit(ts);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ---------------------------------------------------------------- cases

    public void CreateCase(InsiderCase c)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO insider_cases
                (case_id, name, status, justification, legal_basis, requested_by,
                 approved_by, custodians, scope, created_at, expires_at, closed_at, closed_reason)
            VALUES ($id, $name, $status, $just, $legal, $req, $appr, $cust, $scope, $created, $expires, NULL, NULL)
            """;
        cmd.Parameters.AddWithValue("$id", c.CaseId);
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$status", c.Status);
        cmd.Parameters.AddWithValue("$just", c.Justification);
        cmd.Parameters.AddWithValue("$legal", (object?)c.LegalBasis ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$req", c.RequestedBy);
        cmd.Parameters.AddWithValue("$appr", c.ApprovedBy);
        cmd.Parameters.AddWithValue("$cust", JsonSerializer.Serialize(c.Custodians, JsonOpts));
        cmd.Parameters.AddWithValue("$scope", JsonSerializer.Serialize(c.Scope, JsonOpts));
        cmd.Parameters.AddWithValue("$created", c.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", c.ExpiresAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public InsiderCase? GetCase(string caseId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM insider_cases WHERE case_id = $id";
        cmd.Parameters.AddWithValue("$id", caseId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapCase(reader) : null;
    }

    public IReadOnlyList<InsiderCase> ListCases(int limit = 50)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM insider_cases ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        using var reader = cmd.ExecuteReader();

        var list = new List<InsiderCase>();
        while (reader.Read())
            list.Add(MapCase(reader));
        return list;
    }

    public bool CloseCase(string caseId, string reason, DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE insider_cases
               SET status = 'closed', closed_at = $closed, closed_reason = $reason
             WHERE case_id = $id AND status = 'open'
            """;
        cmd.Parameters.AddWithValue("$id", caseId);
        cmd.Parameters.AddWithValue("$closed", now.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", reason);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static InsiderCase MapCase(SqliteDataReader r)
    {
        var custodians = JsonSerializer.Deserialize<List<string>>(
            r.GetString(r.GetOrdinal("custodians")), JsonOpts) ?? [];
        var scope = JsonSerializer.Deserialize<InsiderScope>(
            r.GetString(r.GetOrdinal("scope")), JsonOpts) ?? InsiderScope.Full;

        var closedAtOrdinal = r.GetOrdinal("closed_at");
        var closedReasonOrdinal = r.GetOrdinal("closed_reason");

        return new InsiderCase(
            CaseId: r.GetString(r.GetOrdinal("case_id")),
            Name: r.GetString(r.GetOrdinal("name")),
            Status: r.GetString(r.GetOrdinal("status")),
            Justification: r.GetString(r.GetOrdinal("justification")),
            LegalBasis: r.IsDBNull(r.GetOrdinal("legal_basis")) ? null : r.GetString(r.GetOrdinal("legal_basis")),
            RequestedBy: r.GetString(r.GetOrdinal("requested_by")),
            ApprovedBy: r.GetString(r.GetOrdinal("approved_by")),
            Custodians: custodians,
            Scope: scope,
            CreatedAt: DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
            ExpiresAt: DateTimeOffset.Parse(r.GetString(r.GetOrdinal("expires_at"))),
            ClosedAt: r.IsDBNull(closedAtOrdinal) ? null : DateTimeOffset.Parse(r.GetString(closedAtOrdinal)),
            ClosedReason: r.IsDBNull(closedReasonOrdinal) ? null : r.GetString(closedReasonOrdinal));
    }

    // ---------------------------------------------------------------- audit

    /// <summary>
    /// Añade una entrada al log append-only. Serializado con semáforo para que
    /// dos escrituras concurrentes no lean el mismo prev_hash y bifurquen la cadena.
    /// </summary>
    public async Task AppendAuditAsync(
        string? caseId,
        string actor,
        string tool,
        string? target,
        string? query,
        int resultCount,
        string outcome,
        string? detail,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();

            string prevHash;
            using (var prev = conn.CreateCommand())
            {
                prev.CommandText = "SELECT hash FROM insider_audit ORDER BY seq DESC LIMIT 1";
                prevHash = prev.ExecuteScalar() as string ?? "GENESIS";
            }

            var ts = DateTimeOffset.UtcNow;
            var payload = string.Join('|',
                prevHash,
                ts.ToString("O"),
                caseId ?? "",
                actor,
                tool,
                target ?? "",
                query ?? "",
                resultCount.ToString(),
                outcome,
                detail ?? "");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO insider_audit
                    (ts, case_id, actor, tool, target, query, result_count, outcome, detail, prev_hash, hash)
                VALUES ($ts, $case, $actor, $tool, $target, $query, $count, $outcome, $detail, $prev, $hash)
                """;
            cmd.Parameters.AddWithValue("$ts", ts.ToString("O"));
            cmd.Parameters.AddWithValue("$case", (object?)caseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$actor", actor);
            cmd.Parameters.AddWithValue("$tool", tool);
            cmd.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$query", (object?)query ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$count", resultCount);
            cmd.Parameters.AddWithValue("$outcome", outcome);
            cmd.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prev", prevHash);
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyList<InsiderAuditEntry> ReadAudit(string? caseId, int limit = 200)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = caseId is null
            ? "SELECT * FROM insider_audit ORDER BY seq DESC LIMIT $limit"
            : "SELECT * FROM insider_audit WHERE case_id = $case ORDER BY seq DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
        if (caseId is not null)
            cmd.Parameters.AddWithValue("$case", caseId);

        using var reader = cmd.ExecuteReader();
        var list = new List<InsiderAuditEntry>();
        while (reader.Read())
        {
            list.Add(new InsiderAuditEntry(
                Seq: reader.GetInt64(reader.GetOrdinal("seq")),
                Timestamp: DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ts"))),
                CaseId: reader.IsDBNull(reader.GetOrdinal("case_id")) ? null : reader.GetString(reader.GetOrdinal("case_id")),
                Actor: reader.GetString(reader.GetOrdinal("actor")),
                Tool: reader.GetString(reader.GetOrdinal("tool")),
                Target: reader.IsDBNull(reader.GetOrdinal("target")) ? null : reader.GetString(reader.GetOrdinal("target")),
                Query: reader.IsDBNull(reader.GetOrdinal("query")) ? null : reader.GetString(reader.GetOrdinal("query")),
                ResultCount: reader.GetInt32(reader.GetOrdinal("result_count")),
                Outcome: reader.GetString(reader.GetOrdinal("outcome")),
                Detail: reader.IsDBNull(reader.GetOrdinal("detail")) ? null : reader.GetString(reader.GetOrdinal("detail")),
                PrevHash: reader.GetString(reader.GetOrdinal("prev_hash")),
                Hash: reader.GetString(reader.GetOrdinal("hash"))));
        }
        return list;
    }

    /// <summary>
    /// Recorre la cadena completa y devuelve la primera fila alterada, si la hay.
    /// Coste O(n): úsalo bajo demanda, no en cada request.
    /// </summary>
    public (bool Valid, long Checked, long? BrokenSeq, string? Reason) VerifyAuditChain(int limit = 100_000)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM insider_audit ORDER BY seq ASC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1_000_000));

        using var reader = cmd.ExecuteReader();
        var prev = "GENESIS";
        long checkedCount = 0;

        while (reader.Read())
        {
            var seq = reader.GetInt64(reader.GetOrdinal("seq"));
            var storedPrev = reader.GetString(reader.GetOrdinal("prev_hash"));
            var storedHash = reader.GetString(reader.GetOrdinal("hash"));

            if (storedPrev != prev)
                return (false, checkedCount, seq, $"prev_hash no coincide con el hash de la fila anterior (esperado {prev[..Math.Min(8, prev.Length)]}..., encontrado {storedPrev[..Math.Min(8, storedPrev.Length)]}...)");

            var payload = string.Join('|',
                storedPrev,
                reader.GetString(reader.GetOrdinal("ts")),
                reader.IsDBNull(reader.GetOrdinal("case_id")) ? "" : reader.GetString(reader.GetOrdinal("case_id")),
                reader.GetString(reader.GetOrdinal("actor")),
                reader.GetString(reader.GetOrdinal("tool")),
                reader.IsDBNull(reader.GetOrdinal("target")) ? "" : reader.GetString(reader.GetOrdinal("target")),
                reader.IsDBNull(reader.GetOrdinal("query")) ? "" : reader.GetString(reader.GetOrdinal("query")),
                reader.GetInt32(reader.GetOrdinal("result_count")).ToString(),
                reader.GetString(reader.GetOrdinal("outcome")),
                reader.IsDBNull(reader.GetOrdinal("detail")) ? "" : reader.GetString(reader.GetOrdinal("detail")));

            var recalculated = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            if (recalculated != storedHash)
                return (false, checkedCount, seq, "hash de la fila no coincide: el contenido fue alterado");

            prev = storedHash;
            checkedCount++;
        }

        return (true, checkedCount, null, null);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
