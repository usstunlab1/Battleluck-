using System.Text.Json;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Deterministic operator guardrails for live event starts and production
/// deployment. It uses only server-local counters and verified BattleLuck
/// manifests; it never treats an LLM recommendation as a force token.
/// </summary>
public static class OperatorSafetyService
{
    static readonly string ConfigPath = Path.Combine(ConfigLoader.ConfigRoot, "operator_safety.json");

    public static OperationResult CheckEventStart(string modeId, bool force)
    {
        var config = Load();
        if (!config.Enabled || force)
            return OperationResult.Ok();

        try
        {
            var online = VRisingCore.GetOnlinePlayers().Count(player => player.Exists() && player.IsPlayer());
            var active = BattleLuckPlugin.Session?.ActiveSessions.Count ?? 0;
            var reasons = new List<string>();
            if (config.HighLoadOnlinePlayers > 0 && online >= config.HighLoadOnlinePlayers)
                reasons.Add($"online players {online} >= {config.HighLoadOnlinePlayers}");
            if (config.HighLoadActiveSessions > 0 && active >= config.HighLoadActiveSessions)
                reasons.Add($"active sessions {active} >= {config.HighLoadActiveSessions}");

            if (reasons.Count > 0)
                return OperationResult.Fail($"START_WINDOW_BLOCKED: {string.Join(", ", reasons)}. Add the explicit force argument only after reviewing server load.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"E_RATE: Could not read server load safely: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    public static OperationResult EnsureDeploymentSnapshot(string modeId)
    {
        var config = Load();
        if (!config.ProductionMode || !config.RequireSnapshotBeforeProductionDeploy)
            return OperationResult.Ok();

        var status = new EventDeploymentService().GetStatus(modeId);
        if (status.Success && status.Value?.LatestBackupManifest.Equals("valid", StringComparison.OrdinalIgnoreCase) == true)
            return OperationResult.Ok();

        return OperationResult.Fail($"E_NO_SNAPSHOT: Production deployment of '{modeId}' requires a verified BattleLuck backup manifest. Create a known-good backup before retrying.");
    }

    static OperatorSafetyConfig Load()
    {
        try
        {
            ConfigLoader.EnsureDefaultsDeployed();
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<OperatorSafetyConfig>(File.ReadAllText(ConfigPath), ConfigLoader.JsonOptions)
                    ?? new OperatorSafetyConfig();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[OperatorSafety] Could not load operator_safety.json: {ex.Message}");
        }

        return new OperatorSafetyConfig();
    }
}

public sealed class OperatorSafetyConfig
{
    public bool Enabled { get; set; } = true;
    public int HighLoadOnlinePlayers { get; set; } = 36;
    public int HighLoadActiveSessions { get; set; } = 3;
    public bool ProductionMode { get; set; }
    public bool RequireSnapshotBeforeProductionDeploy { get; set; }
}
