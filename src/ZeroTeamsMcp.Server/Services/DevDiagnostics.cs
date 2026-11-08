namespace ZeroTeamsMcp.Server.Services;

/// <summary>
/// Diagnósticos seguros controlados por MCP_DEV_MODE. Nunca relaja auth/guards/disclosure.
/// </summary>
public static class DevDiagnostics
{
    public static bool Enabled { get; } =
        string.Equals(Environment.GetEnvironmentVariable("MCP_DEV_MODE"), "true", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariable("MCP_DEV_MODE") == "1";

    public static int ApproxResponseChars(string? json) => string.IsNullOrEmpty(json) ? 0 : json.Length;
}
