namespace ZeroTeamsMcp.Core.Models;

public sealed record TeamsMessage(
    string Id,
    string From,
    string Content,
    DateTimeOffset Created,
    string? ReplyToId = null
);
