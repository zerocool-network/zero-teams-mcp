using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ZeroTeamsMcp.Insider.Services;

namespace ZeroTeamsMcp.Insider.Tools;

/// <summary>
/// Guardas de los tools <c>insider_*</c>.
///
/// Regla base: sin caso abierto y aprobado no se lee nada. Las guardas lanzan
/// <see cref="McpException"/> y el wrapper <see cref="RunAsync{T}"/> deja la
/// negativa registrada en el log encadenado.
/// </summary>
internal static class InsiderGuard
{
    /// <summary>Máximo de custodios por caso. Acota el daño de un caso "de barrido".</summary>
    public const int MaxCustodians = 10;

    /// <summary>Ventana temporal máxima por consulta de contenido.</summary>
    public const int MaxWindowDays = 90;

    /// <summary>Máximo de resultados devueltos por consulta.</summary>
    public const int MaxResults = 200;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Kill switch. <c>INSIDER_TOOLS=off</c> desactiva todos los tools del módulo.</summary>
    public static bool Enabled =>
        !string.Equals(Environment.GetEnvironmentVariable("INSIDER_TOOLS"), "off", StringComparison.OrdinalIgnoreCase);

    public static void RequireEnabled(string tool)
    {
        if (!Enabled)
            throw new McpException($"El módulo de investigación está desactivado (INSIDER_TOOLS=off). Tool bloqueado: {tool}.");
    }

    /// <summary>Identidad que ejecuta la acción (nombre de la clave, o fallback local en stdio).</summary>
    public static string Actor(InsiderContext ctx) => ctx.Actor;

    /// <summary>
    /// Valida el token del aprobador (regla de dos personas). Falla cerrado:
    /// si INSIDER_APPROVER_TOKEN no está configurado, no se pueden abrir casos.
    /// </summary>
    public static void ValidateApproverToken(string? approverToken)
    {
        var expected = Environment.GetEnvironmentVariable("INSIDER_APPROVER_TOKEN");

        if (string.IsNullOrWhiteSpace(expected) || expected.Trim().Length < 16)
        {
            throw new McpException(
                "INSIDER_APPROVER_TOKEN no está configurado (o es demasiado corto, mínimo 16 caracteres). " +
                "Sin aprobador designado no se pueden abrir casos de investigación.");
        }

        if (string.IsNullOrWhiteSpace(approverToken) ||
            !CryptographicEquals(approverToken.Trim(), expected.Trim()))
        {
            throw new McpException(
                "approverToken inválido. La apertura de un caso requiere la aprobación de " +
                "una segunda persona (DPO / RRHH / Legal).");
        }
    }

    /// <summary>Quien solicita no puede ser quien aprueba.</summary>
    public static void EnsureTwoPerson(string requestor, string approver)
    {
        if (string.IsNullOrWhiteSpace(approver))
            throw new McpException("approvedBy es obligatorio: identifica a la persona que aprueba el caso.");

        if (string.Equals(requestor.Trim(), approver.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                $"Regla de dos personas: '{requestor}' no puede aprobar su propio caso. " +
                "approvedBy debe ser otra persona (DPO / RRHH / Legal).");
        }
    }

    /// <summary>Recupera un caso y exige que esté abierto, vigente y con el scope pedido.</summary>
    public static InsiderCase RequireOpenCase(InsiderStore store, string? caseId, string scopeKind)
    {
        if (string.IsNullOrWhiteSpace(caseId))
            throw new McpException("caseId es obligatorio. Toda lectura requiere un caso de investigación abierto.");

        var c = store.GetCase(caseId.Trim())
            ?? throw new McpException($"El caso '{caseId}' no existe. Ábrelo con insider_case_open.");

        if (!c.IsOpen)
            throw new McpException($"El caso '{caseId}' está {c.Status}. No se puede consultar.");

        if (c.IsExpired(DateTimeOffset.UtcNow))
            throw new McpException($"El caso '{caseId}' expiró el {c.ExpiresAt:O}. Ábrelo de nuevo con una justificación actualizada.");

        if (!c.Scope.Allows(scopeKind))
            throw new McpException($"El caso '{caseId}' no autoriza el scope '{scopeKind}'.");

        return c;
    }

    /// <summary>El custodio debe estar nominado en el caso. Evita usar un caso como comodín.</summary>
    public static void EnsureCustodian(InsiderCase c, string custodian)
    {
        if (string.IsNullOrWhiteSpace(custodian))
            throw new McpException("El custodio (email o UUID) es obligatorio.");

        var trimmed = custodian.Trim();
        if (!IsValidCustodian(trimmed))
            throw new McpException(
                $"El custodio '{custodian}' no tiene un formato válido (email o UUID). " +
                "Se rechaza para evitar inyección en la ruta de Graph.");

        if (!c.Contains(trimmed))
        {
            throw new McpException(
                $"El custodio '{custodian}' no está nominado en el caso '{c.CaseId}'. " +
                $"Custodios autorizados: {string.Join(", ", c.Custodians)}.");
        }
    }

    /// <summary>Valida que el custodio sea un email o un UUID, sin caracteres de ruta.</summary>
    public static bool IsValidCustodian(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return false;

        // UUID
        if (Guid.TryParse(value, out _))
            return true;

        // Email básico: sin espacios ni caracteres de ruta/control.
        if (value.Contains('@'))
        {
            return !value.Any(ch => char.IsWhiteSpace(ch) || ch is '/' or '\\' or '?' or '#' or '<' or '>' or '"' or '\'' or '\0')
                && value.Count(ch => ch == '@') == 1
                && value.IndexOf('@') > 0
                && value.IndexOf('@') < value.Length - 1;
        }

        return false;
    }

    /// <summary>Ventana temporal acotada. Devuelve el rango efectivo aplicado.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) NormalizeWindow(
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddDays(-30);

        if (start > end)
            throw new McpException("Rango inválido: 'from' es posterior a 'to'.");

        if ((end - start).TotalDays > MaxWindowDays)
            throw new McpException(
                $"Ventana de {(end - start).TotalDays:F0} días supera el máximo de {MaxWindowDays}. " +
                "Acota el rango: una ventana enorme no es proporcional ni útil.");

        return (start, end);
    }

    /// <summary>Acota el número de resultados pedidos.</summary>
    public static int ClampTop(int requested) => Math.Clamp(requested, 1, MaxResults);

    private static bool CryptographicEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}

/// <summary>Resultado de un tool: datos a serializar + cardinalidad para la auditoría.</summary>
internal readonly record struct InsiderResult(object Data, int Count = -1);

/// <summary>
/// Envoltorio único de ejecución: kill switch → trabajo →
/// auditoría (ok / denied / error) → serialización.
/// Ningún tool del módulo puede saltarse el registro.
/// </summary>
internal static class InsiderRunner
{
    public static async Task<string> RunAsync(
        InsiderStore store,
        InsiderContext ctx,
        ILogger logger,
        string tool,
        string? caseId,
        string? target,
        string? query,
        Func<Task<InsiderResult>> work,
        CancellationToken ct = default)
    {
        InsiderGuard.RequireEnabled(tool);

        string actor;
        try
        {
            actor = InsiderGuard.Actor(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo resolver el actor de {Tool}", tool);
            throw;
        }

        try
        {
            var result = await work();

            await store.AppendAuditAsync(
                caseId, actor, tool, target, query, result.Count, "ok", null, ct);

            return JsonSerializer.Serialize(result.Data, InsiderGuard.JsonOpts);
        }
        catch (Exception ex)
        {
            var denied = ex is McpException or ArgumentException;

            // El fallo se audita siempre, ignorando la cancelación: un intento
            // denegado es exactamente lo que hay que poder demostrar después.
            // La auditoría nunca debe enmascarar el error original.
            try
            {
                await store.AppendAuditAsync(
                    caseId, actor, tool, target, query, 0,
                    denied ? "denied" : "error",
                    Truncate(ex.Message, 500),
                    CancellationToken.None);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "No se pudo registrar la auditoría del fallo de {Tool}", tool);
            }

            logger.LogWarning(ex, "insider tool blocked or failed: {Tool} actor={Actor}", tool, actor);
            throw;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
