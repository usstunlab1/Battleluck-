using BattleLuck.Models;
using BattleLuck.Utilities;
using BattleLuck.Services.Flow;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace BattleLuck.Commands
{
    /// <summary>Admin commands to view and manage action logs for debugging.</summary>
    public static class ActionLogCommands
    {
        [Command("actionlog.summary", description: "Show action log summary")]
        public static void ShowLogSummary(ChatCommandContext ctx)
        {
            var summary = FlowActionExecutor.Logger.GetSummary();
            ctx.Reply(summary);
            BattleLuckPlugin.LogInfo(summary);
        }

        [Command("actionlog.recent", description: "Show recent action logs")]
        public static void ShowRecentLogs(ChatCommandContext ctx, int count = 20)
        {
            var entries = FlowActionExecutor.Logger.GetEntries(Math.Max(1, Math.Min(count, 100)));
            ctx.Reply($"Recent {entries.Count} actions:");
            foreach (var entry in entries)
            {
                ctx.Reply(entry.ToString());
            }
        }

        [Command("actionlog.export", description: "Export action logs as JSON")]
        public static void ExportLogs(ChatCommandContext ctx, string? filename = null)
        {
            var json = FlowActionExecutor.Logger.ExportJson();
            var path = filename ?? System.IO.Path.Combine(ConfigLoader.ConfigRoot, "logs", $"actionlog_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                
                System.IO.File.WriteAllText(path, json);
                ctx.Reply($"Logs exported to {path}");
                BattleLuckPlugin.LogInfo($"Action logs exported to {path}");
            }
            catch (Exception ex)
            {
                ctx.Reply($"Export failed: {ex.Message}");
                BattleLuckPlugin.LogError($"Failed to export logs: {ex.Message}");
            }
        }

        [Command("actionlog.clear", description: "Clear action logs")]
        public static void ClearLogs(ChatCommandContext ctx)
        {
            FlowActionExecutor.Logger.Clear();
            ctx.Reply("Action logs cleared");
        }

        [Command("actionlog.filter", description: "Filter logs by phase, status, or action")]
        public static void FilterLogs(ChatCommandContext ctx, string filterKey, string filterValue)
        {
            var entries = FlowActionExecutor.Logger.GetEntries(1000);
            var filtered = entries.Where(e =>
            {
                return filterKey.ToLower() switch
                {
                    "phase" => e.Phase.Equals(filterValue, System.StringComparison.OrdinalIgnoreCase),
                    "status" => e.Status.Equals(filterValue, System.StringComparison.OrdinalIgnoreCase),
                    "action" => e.ActionName.Contains(filterValue, System.StringComparison.OrdinalIgnoreCase),
                    "error" => e.Error?.Contains(filterValue, System.StringComparison.OrdinalIgnoreCase) ?? false,
                    _ => false
                };
            }).ToList();

            ctx.Reply($"Found {filtered.Count} matching logs ({filterKey}={filterValue}):");
            foreach (var entry in filtered.Take(50))
            {
                ctx.Reply(entry.ToString());
            }
        }
    }
}
