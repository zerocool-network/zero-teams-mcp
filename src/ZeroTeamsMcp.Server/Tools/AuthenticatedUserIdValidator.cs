using System.Text.RegularExpressions;

namespace ZeroTeamsMcp.Server.Tools;

internal static class AuthenticatedUserIdValidator
{
    private static readonly Regex UuidRegex = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static void Validate(string? userId, string paramName = "authenticatedUserId")
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var trimmed = userId.Trim();
        if (!UuidRegex.IsMatch(trimmed) && !EmailRegex.IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"{paramName} must be a valid UUID or email address",
                paramName);
        }
    }

    public static string? Normalize(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;
        return userId.Trim().ToLowerInvariant();
    }
}