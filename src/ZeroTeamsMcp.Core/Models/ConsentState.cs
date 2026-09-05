namespace ZeroTeamsMcp.Core.Models;

public enum ConsentStatus
{
    Pending,
    Granted,
    Revoked,
    Failed
}

public sealed class ConsentRecord
{
    public required string TenantId { get; init; }
    public ConsentStatus Status { get; set; } = ConsentStatus.Pending;
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    public DateTime? GrantedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
