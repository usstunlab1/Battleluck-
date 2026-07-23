namespace BattleLuck.Services.Chat;

/// <summary>
/// Builds the compact, server-authored BattleLuck dashboard consumed by an
/// optional ZUI-capable V Rising client. The server remains authoritative:
/// buttons contain normal commands and never mutate client-side state directly.
/// </summary>
public static class BattleLuckZuiDashboard
{
    public static ZuiWindow BuildHome() => new(
        "battleluck.home",
        "BATTLELUCK // SERVER CONTROL",
        new[]
        {
            "Status: ONLINE",
            "Choose a control surface."
        },
        new[]
        {
            new ZuiButton("Events", ".ai ui events"),
            new ZuiButton("Players", ".ai ui players"),
            new ZuiButton("World", ".ai ui world"),
            new ZuiButton("AI Ops", ".ai ui ai"),
            new ZuiButton("Audit", ".ai ui audit")
        });

    public static ZuiWindow BuildSection(string section)
    {
        var normalized = (section ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "events" => Section("events", "EVENT OPERATIONS", "Modes, waves, triggers, objectives", ".ai event status"),
            "players" => Section("players", "PLAYER OPERATIONS", "Sessions, teams, kits, rollback", ".ai player status"),
            "world" => Section("world", "WORLD OPERATIONS", "Zones, tiles, castles, NPCs", ".ai world status"),
            "ai" => Section("ai", "AI OPERATIONS", "Providers, tools, approvals, actions", ".ai status"),
            "audit" => Section("audit", "AUDIT PROGRAM", "100 phases · release readiness", ".ai audit status"),
            _ => BuildHome()
        };
    }

    static ZuiWindow Section(string id, string title, string summary, string statusCommand) => new(
        $"battleluck.{id}",
        title,
        new[] { summary, "All actions are validated server-side." },
        new[]
        {
            new ZuiButton("Status", statusCommand),
            new ZuiButton("Refresh", $".ai ui {id}"),
            new ZuiButton("Back", ".ai ui")
        });
}
