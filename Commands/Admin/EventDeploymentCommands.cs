/// <summary>
/// Chat-facing no-code event deployment commands used by the primary `.ai`
/// interface. Mutation methods are only called from the admin-gated AI path.
/// </summary>
public static class EventDeploymentCommands
{
    static readonly EventDeploymentService Service = new();
    public static EventDeploymentAuditService Audit { get; } = new();

    public static async Task DeployFromGist(ChatCommandContext ctx, string eventId, string gistUrl, bool dryRun = false)
    {
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(gistUrl))
        {
            ctx.Reply("Usage: .ai event deploy <eventId> <https-gist-url> (admin only)");
            return;
        }

        ctx.Reply(dryRun
            ? $"🔎 Dry-running '{eventId}' from the HTTPS Gist. Files are downloaded and validated in memory; no backup, registration, or event start is attempted."
            : $"🤖 Staging '{eventId}' from the HTTPS Gist. Download, validation, backup, and registration are in progress; no live event has started.");
        var result = await Service.DeployFromGistAsync(eventId, gistUrl, dryRun).ConfigureAwait(false);
        Audit.Record("deploy", eventId, gistUrl, result, dryRun: dryRun);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"❌ Event deployment rejected: {result.Error}");
            ctx.Reply("No event start was attempted. Review the validation error or use the offline Safe-Stage workflow.");
            return;
        }

        var deployment = result.Value;
        ctx.Reply(dryRun
            ? $"✅ Dry run passed for '{deployment.ModeId}' ({deployment.ZoneCount} zone(s), hash {deployment.ZoneHash}); registry unchanged."
            : $"✅ Event '{deployment.ModeId}' deployed and registered ({deployment.ZoneCount} zone(s), hash {deployment.ZoneHash}).");
        ctx.Reply(dryRun
            ? "Audit recorded deploy_dry_run with exit=0 and registerOk=false."
            : $"Backup: {deployment.BackupPath}. Start it only after review with `.event.start {deployment.ModeId}`.");
        ctx.Reply("KindredExtract IDs are references only; verify prefabs and queries against a live dump before launch.");
    }

    public static void Status(ChatCommandContext ctx, string eventId = "")
    {
        var result = Service.GetStatus(eventId);
        Audit.RecordStatus(eventId, result);
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
        ctx.Reply($"Zone hashes: {(status.ZoneHashes.Count == 0 ? "none" : string.Join(", ", status.ZoneHashes))}; latest backup: {status.LatestBackup} (manifest {status.LatestBackupManifest}).");
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
        Audit.Record("rollback", eventId, result.Value?.Source, result, rollback: true);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"❌ Event rollback failed: {result.Error}");
            return;
        }

        ctx.Reply($"✅ Event '{result.Value.ModeId}' restored from the last known-good backup and registered again.");
        ctx.Reply("Rollback restores declarative event files; it does not undo a live action that already ran. Verify affected player snapshots after any crash.");
    }

    public static void PurgeBackup(ChatCommandContext ctx, string eventId, string backupId, bool confirmed)
    {
        var result = Service.DeleteBackup(eventId, backupId, confirmed);
        Audit.Record("backup_purge", eventId, result.Value?.Source, result, rollback: true);
        if (!result.Success)
        {
            ctx.Reply($"❌ Backup purge rejected: {result.Error}");
            ctx.Reply("Only BattleLuck deployment backups are eligible; the V Rising world/server save is never deleted by this command.");
            return;
        }

        ctx.Reply($"✅ Deleted BattleLuck deployment backup '{Path.GetFileName(result.Value!.BackupPath)}' for '{result.Value.ModeId}'.");
        ctx.Reply("The V Rising SaveFileManager/world backup was not touched.");
    }

    public static void AuditSummary(ChatCommandContext ctx, string eventId = "")
    {
        var summary = Audit.Summarize(eventId);
        ctx.Reply($"🧠 Event audit ({summary.EventId}): {summary.Records} record(s), failures={summary.Failures}, successful deploys={summary.SuccessfulDeployments}, rollbacks={summary.SuccessfulRollbacks}.");
        if (summary.CommonErrors.Count > 0)
            ctx.Reply($"Common errors: {string.Join(", ", summary.CommonErrors)}.");
        foreach (var recommendation in summary.Recommendations.Take(4))
            ctx.Reply($"Recommendation: {recommendation}");
        if (summary.Recent.Count > 0)
        {
            foreach (var record in summary.Recent)
                ctx.Reply($"[{record.Timestamp.ToLocalTime():MM-dd HH:mm}] {record.Command} {record.EventId}: {(record.Exit == 0 ? "ok" : record.ErrorCode ?? "failed")}");
        }
        else
        {
            ctx.Reply("No event audit records are available yet.");
        }
    }
}
