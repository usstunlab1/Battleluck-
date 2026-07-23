using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services.Flow
{
    /// <summary>
    /// Handles persistence of flow overrides to config files.
    /// Writes effective flows back to session.json with atomic writes and backups.
    /// </summary>
    public static class FlowPersistence
    {
        /// <summary>
        /// Persist the effective flow for a mode and flow type back to session.json.
        /// </summary>
        public static OperationResult PersistFlow(string modeId, FlowType flowType)
        {
            try
            {
                var config = ConfigLoader.Load(modeId);
                var effectiveFlow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowType);

                // Update session.json with the effective flow
                if (flowType == FlowType.Enter)
                {
                    config.Session.Flow.Enter = effectiveFlow;
                }
                else
                {
                    config.Session.Flow.Exit = effectiveFlow;
                }

                // Write to events/{modeId}/session.json (flat config structure)
                var eventsRoot = ModeConfigLoader.EventsRoot;
                var modeDir = Path.Combine(eventsRoot, modeId);
                var sessionPath = Path.Combine(modeDir, "session.json");

                if (!Directory.Exists(modeDir))
                {
                    Directory.CreateDirectory(modeDir);
                }

                // Create backup
                if (File.Exists(sessionPath))
                {
                    var backupPath = sessionPath + ".bak";
                    File.Copy(sessionPath, backupPath, overwrite: true);
                    BattleLuckPlugin.LogInfo($"[FlowPersistence] Created backup: {backupPath}");
                }

                // Atomic write: temp file → replace
                var tmpPath = sessionPath + ".tmp";
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config.Session, options);
                File.WriteAllText(tmpPath, json);

                if (File.Exists(sessionPath))
                {
                    File.Replace(tmpPath, sessionPath, sessionPath + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmpPath, sessionPath, overwrite: true);
                }

                // Reload config
                ConfigLoader.Reload(modeId);

                BattleLuckPlugin.LogInfo($"[FlowPersistence] Persisted {modeId}/{flowType} to events/{modeId}/session.json");
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogError($"[FlowPersistence] Failed to persist {modeId}/{flowType}: {ex.Message}");
                return OperationResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Persist all overrides for a mode (both enter and exit flows).
        /// </summary>
        public static OperationResult PersistMode(string modeId)
        {
            var errors = new List<string>();

            if (FlowOverrideManager.Instance.HasOverride(modeId, FlowType.Enter))
            {
                var result = PersistFlow(modeId, FlowType.Enter);
                if (!result.Success)
                    errors.Add($"Enter flow: {result.Error}");
            }

            if (FlowOverrideManager.Instance.HasOverride(modeId, FlowType.Exit))
            {
                var result = PersistFlow(modeId, FlowType.Exit);
                if (!result.Success)
                    errors.Add($"Exit flow: {result.Error}");
            }

            if (errors.Count > 0)
            {
                return OperationResult.Fail(string.Join("; ", errors));
            }

            return OperationResult.Ok();
        }

        /// <summary>
        /// Persist all overrides across all modes.
        /// </summary>
        public static OperationResult PersistAll()
        {
            var registry = BattleLuckPlugin.GameModes;
            if (registry == null)
            {
                return OperationResult.Fail("Game mode registry not available");
            }

            var allModes = registry.GetRegisteredModes();
            var errors = new List<string>();

            foreach (var modeId in allModes)
            {
                var result = PersistMode(modeId);
                if (!result.Success)
                {
                    errors.Add($"{modeId}: {result.Error}");
                }
            }

            if (errors.Count > 0)
            {
                return OperationResult.Fail(string.Join("; ", errors));
            }

            BattleLuckPlugin.LogInfo($"[FlowPersistence] Persisted all modes ({allModes.Count()})");
            return OperationResult.Ok();
        }

        /// <summary>
        /// Check if a mode has any overrides that need persisting.
        /// </summary>
        public static bool HasPendingOverrides(string modeId)
        {
            return FlowOverrideManager.Instance.HasOverride(modeId, FlowType.Enter) ||
                   FlowOverrideManager.Instance.HasOverride(modeId, FlowType.Exit);
        }

        /// <summary>
        /// Get a summary of all pending overrides.
        /// </summary>
        public static string GetPendingOverridesSummary()
        {
            var overrides = FlowOverrideManager.Instance.GetAllOverrides();
            var summary = new List<string>();

            foreach (var kvp in overrides)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2)
                {
                    summary.Add($"{parts[0]}/{parts[1]}");
                }
            }

            return summary.Count > 0 ? string.Join(", ", summary) : "No pending overrides";
        }
    }
}
