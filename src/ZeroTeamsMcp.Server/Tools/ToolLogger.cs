using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace ZeroTeamsMcp.Server.Tools;

internal static class ToolLogger
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task<string> ExecuteWithLogging<T>(
        this ILogger logger,
        string toolName,
        string? userId,
        Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        logger.LogInformation(
            "MCP tool started: {ToolName}, requestId={RequestId}, userId={UserId}",
            toolName, requestId, HashUserId(userId));

        try
        {
            var result = await action();
            stopwatch.Stop();

            logger.LogInformation(
                "MCP tool completed: {ToolName}, requestId={RequestId}, durationMs={DurationMs}, success=true",
                toolName, requestId, stopwatch.ElapsedMilliseconds);

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            logger.LogError(ex,
                "MCP tool failed: {ToolName}, requestId={RequestId}, durationMs={DurationMs}, error={Error}",
                toolName, requestId, stopwatch.ElapsedMilliseconds, ex.Message);

            throw;
        }
    }

    private static string? HashUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(userId));
        return Convert.ToHexString(hash)[..8];
    }
}