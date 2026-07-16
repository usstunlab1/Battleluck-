using BattleLuck.Services.Runtime;

/// <summary>
/// Chat-facing no-code event deployment commands used by the primary `.ai`
/// interface. Mutation methods are only called from the admin-gated AI path.
/// </summary>
public static class EventDeploymentCommands
{
    static readonly EventDeploymentService Service = new();

    public static async Task DeployFromGist(ChatCommandContext ctx, string eventId, string gistUrl)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(gistUrl))
        {
            ctx.Reply("Usage: .ai event deploy <eventId> <https-gist-url> (admin only)");
            return;
        }

        ctx.Reply($"🤖 Staging '{eventId}' from the HTTPS Gist. Download, validation, backup, and registration are in progress; no live event has started.");
        var result = await Service.DeployFromGistAsync(eventId, gistUrl).ConfigureAwait(false);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"❌ Event deployment rejected: {result.Error}");
            ctx.Reply("No event start was attempted. Review the validation error or use the offline Safe-Stage workflow.");
            return;
        }

        var deployment = result.Value;
        ctx.Reply($"✅ Event '{deployment.ModeId}' deployed and registered ({deployment.ZoneCount} zone(s), hash {deployment.ZoneHash}).");
        ctx.Reply($"Backup: {deployment.BackupPath}. Start it only after review with `.event.start {deployment.ModeId}`.");
        ctx.Reply("KindredExtract IDs are references only; verify prefabs and queries against a live dump before launch.");
    }

    public static void Status(ChatCommandContext ctx, string eventId = "")
    {
        var result = Service.GetStatus(eventId);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"❌ Event status failed: {result.Error}");
            return;
        }

        var status = result.Value;
        if (status.ModeId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply($"📋 Event deployment status: {status.EventCount} event folder(s), registered={status.Registered}, valid={status.FlowValid}, hashes={status.ZoneHashes.Count}.");
            ctx.Reply("Use `.ai event status <eventId>` for file and validation details.");
            return;
        }

        ctx.Reply($"📋 Event '{status.ModeId}': directory={(status.HasDirectory ? "yes" : "no")}, files={(status.HasAllFiles ? "complete" : "incomplete")}, valid={(status.FlowValid ? "yes" : "no")}, registered={(status.Registered ? "yes" : "no")}.");
        ctx.Reply($"Zone hashes: {(status.ZoneHashes.Count == 0 ? "none" : string.Join(", ", status.ZoneHashes))}; latest backup: {status.LatestBackup}.");
        foreach (var error in status.Errors.Take(6))
            ctx.Reply($"⚠️ {error}");
    }

    public static void Rollback(ChatCommandContext ctx, string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            ctx.Reply("Usage: .ai event rollback <eventId> (admin only)");
            return;
        }

        var result = Service.Rollback(eventId);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"❌ Event rollback failed: {result.Error}");
            return;
        }

        ctx.Reply($"✅ Event '{result.Value.ModeId}' restored from the last known-good backup and registered again.");
        ctx.Reply("Rollback restores declarative event files; it does not undo a live action that already ran. Verify affected player snapshots after any crash.");
    }
}
