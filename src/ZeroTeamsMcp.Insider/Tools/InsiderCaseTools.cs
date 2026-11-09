using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ZeroTeamsMcp.Core.Auth;
using ZeroTeamsMcp.Insider.Services;

namespace ZeroTeamsMcp.Insider.Tools;

/// <summary>
/// Gobierno de los casos de investigación: apertura con doble aprobación,
/// cierre, consulta y verificación del log encadenado.
/// </summary>
[McpServerToolType]
public sealed class InsiderCaseTools
{
    private const int MinJustificationChars = 20;
    private const int MaxCaseDays = 90;

    [McpServerTool(Name = "insider_case_open")]
    [Description(
        "Abre un caso de investigación interna. Requiere doble aprobación: quien solicita (identidad del cliente) " +
        "no puede ser quien aprueba (approvedBy + approverToken del servidor). Sin caso abierto, ningún otro tool " +
        "insider_* puede leer datos. Devuelve el caseId a usar en el resto de consultas.")]
    public static async Task<string> CaseOpen(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger,
        [Description("Nombre corto del caso, ej. 'Posible exfiltración — equipo comercial'.")] string name,
        [Description("Justificación concreta del caso (mínimo 20 caracteres). Queda en el log de auditoría.")] string justification,
        [Description("Emails o UUIDs de los empleados investigados, separados por comas. Máximo 10.")] string custodians,
        [Description("Email de la persona que aprueba el caso (DPO / RRHH / Legal). Debe ser distinta de quien lo solicita.")] string approvedBy,
        [Description("Token del aprobador configurado en el servidor (INSIDER_APPROVER_TOKEN).")] string approverToken,
        [Description("Opcional. Base legal o referencia del procedimiento interno (ej. 'Política uso aceptado v3, art. 20.3 ET').")] string? legalBasis = null,
        [Description("Opcional. Días de vigencia del caso. Default 30, máximo 90.")] int days = 30,
        [Description("Opcional. Autoriza lectura de buzón. Default true.")] bool includeMail = true,
        [Description("Opcional. Autoriza lectura de chats de Teams. Default true.")] bool includeChat = true,
        [Description("Opcional. Autoriza revisión de OneDrive/SharePoint. Default true.")] bool includeFiles = true,
        [Description("Opcional. Autoriza señales de auditoría. Default true.")] bool includeSignals = true)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_case_open",
            caseId: null, target: null, query: name, async () =>
            {
                InsiderGuard.ValidateApproverToken(approverToken);

                var actor = InsiderGuard.Actor(ctx);
                InsiderGuard.EnsureTwoPerson(actor, approvedBy);

                if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
                    throw new McpException("name es obligatorio (mínimo 3 caracteres).");

                if (string.IsNullOrWhiteSpace(justification) || justification.Trim().Length < MinJustificationChars)
                    throw new McpException(
                        $"justification es obligatoria y debe tener al menos {MinJustificationChars} caracteres. " +
                        "'Sospecha' no justifica acceder a buzones ajenos.");

                var list = custodians
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (list.Count == 0)
                    throw new McpException("custodians no puede estar vacío.");

                if (list.Count > InsiderGuard.MaxCustodians)
                    throw new McpException(
                        $"Máximo {InsiderGuard.MaxCustodians} custodios por caso (pedidos: {list.Count}). " +
                        "Un caso que abarca media plantilla no es proporcional.");

                var effectiveDays = Math.Clamp(days, 1, MaxCaseDays);
                var now = DateTimeOffset.UtcNow;

                var scope = new InsiderScope(includeMail, includeChat, includeFiles, includeSignals);
                if (!scope.Mail && !scope.Chat && !scope.Files && !scope.Signals)
                    throw new McpException("El caso no autoriza ningún scope: no tendría sentido abrirlo.");

                var caseId = $"CASE-{now:yyyyMMdd}-{RandomSuffix()}";

                var created = new InsiderCase(
                    CaseId: caseId,
                    Name: name.Trim(),
                    Status: "open",
                    Justification: justification.Trim(),
                    LegalBasis: string.IsNullOrWhiteSpace(legalBasis) ? null : legalBasis.Trim(),
                    RequestedBy: actor,
                    ApprovedBy: approvedBy.Trim(),
                    Custodians: list,
                    Scope: scope,
                    CreatedAt: now,
                    ExpiresAt: now.AddDays(effectiveDays),
                    ClosedAt: null,
                    ClosedReason: null);

                store.CreateCase(created);

                // El propio acto de apertura queda auditado dentro del wrapper.
                return new InsiderResult(new
                {
                    created.CaseId,
                    created.Name,
                    created.Status,
                    created.RequestedBy,
                    created.ApprovedBy,
                    created.Custodians,
                    Scope = new { created.Scope.Mail, created.Scope.Chat, created.Scope.Files, created.Scope.Signals },
                    created.CreatedAt,
                    created.ExpiresAt,
                    created.LegalBasis,
                    Note = "Caso abierto. Toda lectura queda registrada en el log encadenado (insider_audit_trail)."
                }, list.Count);
            });
    }

    [McpServerTool(Name = "insider_case_list")]
    [Description("Lista los casos de investigación o detalla uno concreto. Sin caseId devuelve los últimos casos.")]
    public static async Task<string> CaseList(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger,
        [Description("Opcional. ID del caso a detallar.")] string? caseId = null,
        [Description("Opcional. Máximo de casos a listar. Default 20.")] int limit = 20)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_case_list",
            caseId, target: null, query: null, async () =>
            {
                if (!string.IsNullOrWhiteSpace(caseId))
                {
                    var c = store.GetCase(caseId.Trim())
                        ?? throw new McpException($"El caso '{caseId}' no existe.");
                    return new InsiderResult(c, 1);
                }

                var cases = store.ListCases(limit);
                return new InsiderResult(cases, cases.Count);
            });
    }

    [McpServerTool(Name = "insider_case_close")]
    [Description("Cierra un caso y deja de permitir consultas sobre él. Requiere el token del aprobador.")]
    public static async Task<string> CaseClose(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger,
        [Description("ID del caso a cerrar.")] string caseId,
        [Description("Motivo del cierre (queda auditado).")] string reason,
        [Description("Token del aprobador (INSIDER_APPROVER_TOKEN).")] string approverToken)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_case_close",
            caseId, target: null, query: reason, async () =>
            {
                InsiderGuard.ValidateApproverToken(approverToken);

                if (string.IsNullOrWhiteSpace(reason))
                    throw new McpException("reason es obligatorio para cerrar un caso.");

                var closed = store.CloseCase(caseId.Trim(), reason.Trim(), DateTimeOffset.UtcNow);
                if (!closed)
                    throw new McpException($"No se pudo cerrar '{caseId}': no existe o ya estaba cerrado.");

                return new InsiderResult(new { caseId, Status = "closed", Reason = reason.Trim() }, 1);
            });
    }

    [McpServerTool(Name = "insider_audit_trail")]
    [Description("Devuelve el log de auditoría encadenado (quién consultó qué, cuándo, con qué caso y resultado).")]
    public static async Task<string> AuditTrail(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger,
        [Description("Opcional. Filtra por caso. Sin caseId devuelve todo el log.")] string? caseId = null,
        [Description("Opcional. Máximo de entradas. Default 100.")] int limit = 100)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_audit_trail",
            caseId, target: null, query: null, async () =>
            {
                var entries = store.ReadAudit(
                    string.IsNullOrWhiteSpace(caseId) ? null : caseId.Trim(),
                    Math.Clamp(limit, 1, 1000));

                return new InsiderResult(new { Count = entries.Count, Entries = entries }, entries.Count);
            });
    }

    [McpServerTool(Name = "insider_audit_verify")]
    [Description("Verifica la integridad de la cadena de auditoría. Detecta ediciones o borrados de entradas.")]
    public static async Task<string> AuditVerify(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_audit_verify",
            null, target: null, query: null, () =>
            {
                var (valid, checkedCount, brokenSeq, reason) = store.VerifyAuditChain();

                return Task.FromResult(new InsiderResult(new
                {
                    ChainValid = valid,
                    EntriesChecked = checkedCount,
                    BrokenAtSeq = brokenSeq,
                    Reason = reason,
                    Verdict = valid
                        ? "Cadena íntegra: ninguna entrada fue alterada."
                        : "CADENA ROTA: la auditoría no es fiable a partir de la secuencia indicada."
                }, (int)checkedCount));
            });
    }

    [McpServerTool(Name = "insider_permissions_check")]
    [Description(
        "Diagnóstico: muestra los roles app-only que el token tiene realmente y qué tools insider_* " +
        "pueden funcionar con ellos. Úsalo antes de prometer una investigación que el permiso no soporta.")]
    public static async Task<string> PermissionsCheck(
        InsiderStore store,
        InsiderContext ctx,
        ILogger<InsiderCaseTools> logger,
        GraphAuthProvider authProvider,
        CancellationToken ct = default)
    {
        return await InsiderRunner.RunAsync(store, ctx, logger, "insider_permissions_check",
            null, target: null, query: null, async () =>
            {
                var token = await authProvider.GetAccessTokenAsync(ct);
                var roles = ReadTokenRoles(token);

                var definitions = new (string Tool, string Description, string[] Required)[]
                {
                    ("insider_mail_search", "Lectura de buzones (asunto, remitente, cuerpo opcional)", ["Mail.Read"]),
                    ("insider_forwarding_rules", "Reglas de reenvío a externos", ["Mail.Read"]),
                    ("insider_chat_search", "Chats 1:1 y grupales de Teams", ["Chat.ReadWrite.All", "Chat.Read.All"]),
                    ("insider_external_sharing", "Enlaces anónimos y permisos externos en OneDrive", ["Files.Read.All"]),
                    ("insider_audit_search", "Audit log de Purview (operaciones por usuario)", ["AuditLog.Read.All"]),
                    ("insider_chat_channels", "Mensajes de canales de Teams", ["ChannelMessage.Read.All"]),
                    ("ediscovery", "Búsqueda cross-mailbox y legal hold", ["eDiscovery.Read.All"]),
                    ("insider_signin_risk", "Riesgo de inicio de sesión (Identity Protection)", ["IdentityRiskEvent.Read.All", "IdentityRiskyUser.Read.All"])
                };

                var capabilities = definitions
                    .Select(d => new
                    {
                        Tool = d.Tool,
                        Description = d.Description,
                        Required = d.Required,
                        Available = d.Required.Any(roles.Contains),
                        Missing = d.Required.Where(r => !roles.Contains(r)).ToArray()
                    })
                    .ToList();

                return new InsiderResult(new
                {
                    RolesGranted = roles,
                    Capabilities = capabilities,
                    ToolsEnabled = InsiderGuard.Enabled,
                    Warning = "Mail.Read y Files.Read.All son tenant-wide. Sin Application Access Policy, " +
                              "este servidor puede leer todos los buzones y todos los OneDrive de la organización.",
                    ManualCheck = "New-ApplicationAccessPolicy en Exchange Online PowerShell para acotar Mail.Read a un grupo."
                }, capabilities.Count(n => n.Available));
            });
    }

    private static HashSet<string> ReadTokenRoles(string jwt)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return roles;

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        if (doc.RootElement.TryGetProperty("roles", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray())
            {
                var v = r.GetString();
                if (!string.IsNullOrWhiteSpace(v))
                    roles.Add(v);
            }
        }
        return roles;
    }

    private static string RandomSuffix()
    {
        Span<byte> buf = stackalloc byte[3];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf);
    }
}
