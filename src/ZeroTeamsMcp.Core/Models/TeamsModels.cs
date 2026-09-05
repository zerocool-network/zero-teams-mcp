namespace ZeroTeamsMcp.Core.Models;

public sealed record ChatInfo(
    string Id,
    string? DisplayName,
    string ChatType,
    string? WebUrl,
    DateTimeOffset? CreatedDateTime
);

public sealed record ChannelInfo(
    string Id,
    string DisplayName,
    string? Description,
    string? WebUrl,
    string ChannelType,
    DateTimeOffset? CreatedDateTime
);

public sealed record ChatMessageInfo(
    string Id,
    string? From,
    string? FromUserId,
    string Content,
    string? ContentType,
    DateTimeOffset? CreatedDateTime,
    string? ReplyToId
);

public sealed record MemberInfo(
    string Id,
    string? DisplayName,
    string? Email,
    string? UserId,
    string Roles
);
