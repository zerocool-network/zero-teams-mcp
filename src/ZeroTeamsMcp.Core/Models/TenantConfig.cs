namespace ZeroTeamsMcp.Core.Models;

public sealed class TenantConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
