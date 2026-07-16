using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleLuck.Core;
using BattleLuck.Core.Loaders;
using BattleLuck.Services.Modes;

/// <summary>
/// Central registry for all game modes. Maps mode IDs to mode instances.
/// </summary>
public sealed class GameModeRegistry
{
    private readonly Dictionary<string, GameModeEngine> _modes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a game mode. Replaces any existing mode with the same ID.</summary>
    public void Register(GameModeEngine mode)
    {
        _modes[mode.ModeId] = mode;
        BattleLuckPlugin.LogInfo($"[GameModeRegistry] Registered mode: {mode.ModeId} ({mode.DisplayName})");
    }

    /// <summary>Resolve a mode by its ID.</summary>
    public GameModeEngine? Resolve(string modeId)
    {
        return _modes.TryGetValue(modeId, out var mode) ? mode : null;
    }

    /// <summary>Check if a mode is registered.</summary>
    public bool IsRegistered(string modeId) => _modes.ContainsKey(modeId);

    /// <summary>Get all registered mode IDs.</summary>
    public IReadOnlyCollection<string> GetRegisteredModes() => _modes.Keys;

    /// <summary>Get all registered modes.</summary>
    public IReadOnlyDictionary<string, GameModeEngine> All => _modes;

    public static List<ModeInfo> LoadAllModes()
    {
        var modes = new List<ModeInfo>();
        var eventsRoot = Path.Combine(ConfigLoader.ConfigRoot, "events");
        if (!Directory.Exists(eventsRoot))
            return modes;

        var seenModeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(eventsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(path);
                var definition = System.Text.Json.JsonSerializer.Deserialize<UnifiedEventDefinition>(json, ConfigLoader.JsonOptions);
                if (definition == null)
                {
                    BattleLuckPlugin.LogWarning($"[GameModeRegistry] Unified event file '{path}' was empty or invalid JSON.");
                    continue;
                }

                var modeId = string.IsNullOrWhiteSpace(definition.Metadata.Id) ? Path.GetFileNameWithoutExtension(path) : definition.Metadata.Id;
                if (string.IsNullOrWhiteSpace(modeId))
                {
                    BattleLuckPlugin.LogWarning($"[GameModeRegistry] Unified event file '{path}' is missing metadata.id.");
                    continue;
                }

                if (!definition.Metadata.Enabled)
                    continue;

                if (!seenModeIds.Add(modeId))
                {
                    BattleLuckPlugin.LogWarning($"[GameModeRegistry] Duplicate mode ID '{modeId}' found in '{path}'. Skipping.");
                    continue;
                }

                modes.Add(new ModeInfo
                {
                    ModeId = modeId,
                    DisplayName = string.IsNullOrWhiteSpace(definition.Metadata.DisplayName) ? modeId : definition.Metadata.DisplayName,
                    Version = int.TryParse(definition.Metadata.Version, out var version) ? version : 1,
                    Description = ""
                });
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[GameModeRegistry] Failed to parse unified event '{path}': {ex.Message}");
            }
        }

        return modes
            .OrderBy(m => m.ModeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static ManifestConfig? LoadManifest(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<ManifestConfig>(json, ConfigLoader.JsonOptions);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[GameModeRegistry] Failed to parse manifest '{path}': {ex.Message}");
            return null;
        }
    }
}

public sealed class ModeInfo
{
    public string ModeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Description { get; set; } = "";
}
