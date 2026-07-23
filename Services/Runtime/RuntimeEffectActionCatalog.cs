using System.Collections;
using System.Reflection;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Runtime action descriptors for optional, request-scoped effects. The entries
/// are injected into ActionManifestService so AI prompts and event validation see
/// the same actions that the runtime Harmony adapter executes.
/// </summary>
public static class RuntimeEffectActionCatalog
{
    public sealed record Descriptor(
        string Name,
        string Description,
        string RiskLevel,
        bool RequiresApproval,
        string[] Required,
        string[] Optional,
        string[] Examples);

    public static readonly IReadOnlyDictionary<string, Descriptor> Entries =
        new Dictionary<string, Descriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["effect.assign"] = new(
                "effect.assign",
                "Assign a temporary buff, glow, attached VFX, world VFX, or border VFX to a request-selected target.",
                "controlled",
                true,
                new[] { "prefab" },
                new[] { "effectId", "kind", "targetType", "target", "trackingGroup", "durationSeconds", "cleanup", "zoneHash", "position", "pointId", "spacing", "maxPoints", "heightOffset" },
                new[] { "effect.assign:prefab=Buff_Blood_Glow|kind=glow|targetType=self|durationSeconds=30" }),
            ["effect.remove"] = new(
                "effect.remove",
                "Remove a runtime effect assignment by id, tracking group, target, or prefab.",
                "safe",
                false,
                Array.Empty<string>(),
                new[] { "effectId", "trackingGroup", "prefab", "targetType", "target" },
                new[] { "effect.remove:effectId=blood_glow" }),
            ["effect.replace"] = new(
                "effect.replace",
                "Remove matching runtime assignments and apply a replacement effect.",
                "controlled",
                true,
                new[] { "prefab" },
                new[] { "effectId", "kind", "targetType", "trackingGroup", "durationSeconds", "cleanup" },
                new[] { "effect.replace:effectId=border_glow|prefab=Buff_Frost_Glow|targetType=zone_border" }),
            ["effect.status"] = new(
                "effect.status",
                "Report active runtime effects for the current player or event session.",
                "safe",
                false,
                Array.Empty<string>(),
                new[] { "effectId", "trackingGroup", "targetType" },
                new[] { "effect.status" }),
            ["effect.clear_group"] = new(
                "effect.clear_group",
                "Remove every runtime effect owned by a tracking group.",
                "controlled",
                true,
                new[] { "trackingGroup" },
                Array.Empty<string>(),
                new[] { "effect.clear_group:trackingGroup=stage_two_border" }),

            ["zone.border.effect.apply"] = new(
                "zone.border.effect.apply",
                "Spawn a tracked visual prefab around the active zone border using real world X/Z coordinates.",
                "controlled",
                true,
                new[] { "prefab" },
                new[] { "effectId", "zoneHash", "spacing", "maxPoints", "heightOffset", "durationSeconds", "cleanup", "trackingGroup" },
                new[] { "zone.border.effect.apply:prefab=Buff_Blood_Glow|spacing=5|cleanup=on_event_exit" }),
            ["zone.border.effect.remove"] = new(
                "zone.border.effect.remove",
                "Remove request-created zone border effects.",
                "safe",
                false,
                Array.Empty<string>(),
                new[] { "effectId", "trackingGroup", "prefab" },
                new[] { "zone.border.effect.remove:trackingGroup=bloodbath_border_glow" }),
            ["zone.border.effect.replace"] = new(
                "zone.border.effect.replace",
                "Replace a tracked zone border effect with another prefab.",
                "controlled",
                true,
                new[] { "prefab" },
                new[] { "effectId", "trackingGroup", "spacing", "maxPoints", "heightOffset", "durationSeconds", "cleanup" },
                new[] { "zone.border.effect.replace:trackingGroup=arena_border|prefab=Buff_Frost_Glow" }),
            ["zone.border.effect.status"] = new(
                "zone.border.effect.status",
                "Report active request-created effects on the current zone border.",
                "safe",
                false,
                Array.Empty<string>(),
                new[] { "effectId", "trackingGroup" },
                new[] { "zone.border.effect.status" }),

            ["spawn.effect.assign"] = new(
                "spawn.effect.assign",
                "Apply a request-scoped effect to entities in a spawned or tracked group.",
                "controlled",
                true,
                new[] { "prefab" },
                new[] { "effectId", "trackingGroup", "targetType", "durationSeconds", "cleanup" },
                new[] { "spawn.effect.assign:prefab=Buff_General_Berserk|trackingGroup=spawned|durationSeconds=120" }),
            ["spawn.effect.remove"] = new(
                "spawn.effect.remove",
                "Remove an effect from spawned or tracked entities.",
                "safe",
                false,
                Array.Empty<string>(),
                new[] { "effectId", "trackingGroup", "prefab" },
                new[] { "spawn.effect.remove:trackingGroup=spawned" }),
            ["tracking.group.effect.apply"] = new(
                "tracking.group.effect.apply",
                "Apply an effect to every live entity in an existing event tracking group.",
                "controlled",
                true,
                new[] { "prefab", "trackingGroup" },
                new[] { "effectId", "durationSeconds", "cleanup" },
                new[] { "tracking.group.effect.apply:prefab=Buff_Blood_Glow|trackingGroup=bosses" }),
            ["tracking.group.effect.remove"] = new(
                "tracking.group.effect.remove",
                "Remove effects assigned through an event tracking group.",
                "safe",
                false,
                new[] { "trackingGroup" },
                new[] { "effectId", "prefab" },
                new[] { "tracking.group.effect.remove:trackingGroup=bosses" })
        };

    public static bool IsRuntimeEffectAction(string? actionName) =>
        !string.IsNullOrWhiteSpace(actionName) && Entries.ContainsKey(actionName.Trim());

    public static void InjectEntries(IDictionary dictionary, Type valueType)
    {
        foreach (var descriptor in Entries.Values)
        {
            if (dictionary.Contains(descriptor.Name))
                continue;

            var entry = Activator.CreateInstance(valueType);
            if (entry == null)
                continue;

            Set(entry, "Name", descriptor.Name);
            Set(entry, "Category", "effect");
            Set(entry, "Description", descriptor.Description);
            Set(entry, "RiskLevel", descriptor.RiskLevel);
            Set(entry, "RequiresApproval", descriptor.RequiresApproval);
            Set(entry, "HandlerAvailable", true);
            Set(entry, "Executable", true);
            Set(entry, "MainThreadRequired", true);
            Set(entry, "Availability", "available");
            AddStrings(entry, "Required", descriptor.Required);
            AddStrings(entry, "Optional", descriptor.Optional);
            AddStrings(entry, "Examples", descriptor.Examples);

            dictionary[descriptor.Name] = entry;
        }
    }

    static void Set(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite != true)
            return;
        try { property.SetValue(target, value); } catch { }
    }

    static void AddStrings(object target, string propertyName, IEnumerable<string> values)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(target) is not IList list)
            return;
        foreach (var value in values)
        {
            if (!list.Cast<object?>().Any(existing => string.Equals(existing?.ToString(), value, StringComparison.OrdinalIgnoreCase)))
                list.Add(value);
        }
    }
}
