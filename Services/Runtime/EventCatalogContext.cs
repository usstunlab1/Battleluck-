using System.Text.Json;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Manages an event-scoped catalog context that holds only the NPC, wave, reward,
/// and drill entries allowed by the active event definition. The entire schematic
/// library is never loaded for every event — only the entries explicitly permitted.
/// </summary>
public sealed class EventCatalogContextService
{
    public static EventCatalogContextService Instance { get; } = new();

    AdaptiveEventCatalogRoot? _fullCatalog;
    readonly string _catalogPath;
    readonly object _lock = new();

    public EventCatalogContextService()
    {
        _catalogPath = Path.Combine(ConfigLoader.ConfigRoot, "adaptive_event_catalog.json");
    }

    /// <summary>
    /// Load the full adaptive event catalog from disk.
    /// </summary>
    AdaptiveEventCatalogRoot LoadFullCatalog()
    {
        if (_fullCatalog != null) return _fullCatalog;

        lock (_lock)
        {
            if (_fullCatalog != null) return _fullCatalog;

            try
            {
                if (File.Exists(_catalogPath))
                {
                    var json = File.ReadAllText(_catalogPath);
                    _fullCatalog = JsonSerializer.Deserialize<AdaptiveEventCatalogRoot>(json, ConfigLoader.JsonOptions);
                }
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[EventCatalogContext] Failed to load catalog: {ex.Message}");
            }

            return _fullCatalog ??= new AdaptiveEventCatalogRoot();
        }
    }

    /// <summary>
    /// Build an event-scoped catalog context for the given event mode ID.
    /// Only entries explicitly allowed by the event definition are included.
    /// </summary>
    public EventCatalogContext BuildContext(string modeId)
    {
        var full = LoadFullCatalog();

        // Find the event configuration
        if (!full.Events.TryGetValue(modeId, out var eventConfig) || !eventConfig.Enabled)
        {
            BattleLuckPlugin.LogInfo($"[EventCatalogContext] No adaptive config for event '{modeId}', using defaults.");
            return new EventCatalogContext { EventId = modeId };
        }

        // Resolve NPC catalog entries allowed by this event
        var allowedNpcIds = new HashSet<string>(eventConfig.Npcs.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var npcDict = new Dictionary<string, NpcCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        // First check event-specific NPC entries
        foreach (var npc in eventConfig.Npcs)
        {
            if (!string.IsNullOrWhiteSpace(npc.Id))
                npcDict[npc.Id] = npc;
        }

        // Then check global NPC definitions that are allowed for this event
        foreach (var npc in full.NpcDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(npc.Id) &&
                !npcDict.ContainsKey(npc.Id) &&
                npc.AllowedEvents.Count > 0 &&
                npc.AllowedEvents.Any(e => e.Equals(modeId, StringComparison.OrdinalIgnoreCase) || e.Equals("*")))
            {
                npcDict[npc.Id] = npc;
            }
        }

        // Build wave schematics dictionary
        var waveDict = new Dictionary<string, WaveSchematic>(StringComparer.OrdinalIgnoreCase);
        foreach (var wave in eventConfig.Waves)
        {
            if (!string.IsNullOrWhiteSpace(wave.Id))
                waveDict[wave.Id] = wave;
        }

        // Build reward profiles dictionary
        var rewardDict = new Dictionary<string, RewardProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var rp in full.RewardProfiles)
        {
            if (!string.IsNullOrWhiteSpace(rp.Id) &&
                (eventConfig.Rewards.AllowedRewardProfiles.Count == 0 ||
                 eventConfig.Rewards.AllowedRewardProfiles.Any(r => r.Equals(rp.Id, StringComparison.OrdinalIgnoreCase))))
            {
                rewardDict[rp.Id] = rp;
            }
        }

        // Build drills dictionary
        var drillDict = new Dictionary<string, CombatDrillDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var drill in eventConfig.Drills)
        {
            if (!string.IsNullOrWhiteSpace(drill.Id))
                drillDict[drill.Id] = drill;
        }

        return new EventCatalogContext
        {
            EventId = modeId,
            Npcs = npcDict,
            Waves = waveDict,
            Rewards = rewardDict,
            Drills = drillDict
        };
    }

    /// <summary>
    /// Reload the catalog from disk (for hot-reload scenarios).
    /// </summary>
    public void ReloadCatalog()
    {
        lock (_lock)
        {
            _fullCatalog = null;
        }
    }
}