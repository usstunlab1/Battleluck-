using System.IO;
using System.Linq;
using System.Text.Json;
using BattleLuck.Core.Loaders;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

public sealed class EventDefinitionLoader
{
    readonly ActionManifestService _actions = new();
    readonly CustomSequenceService _customSequences = new();

    public string EventsRoot => Path.Combine(ConfigLoader.ConfigRoot, "events");

    public bool TryLoad(string modeId, out UnifiedEventDefinition? definition, out EventValidationResult validation)
    {
        definition = null;
        validation = new EventValidationResult();

        var path = Path.Combine(EventsRoot, modeId, "flow.json");
        if (!File.Exists(path))
        {
            // Fallback to legacy path for transition
            path = Path.Combine(EventsRoot, $"{modeId}.json");
            if (!File.Exists(path))
                return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            definition = JsonSerializer.Deserialize<UnifiedEventDefinition>(json, ConfigLoader.JsonOptions);
            if (definition == null)
            {
                validation.Errors.Add($"Unified event '{modeId}' was empty or invalid JSON.");
                return true;
            }

            Validate(definition, validation);
            foreach (var warning in validation.Warnings)
                BattleLuckPlugin.LogWarning($"[EventDefinitionLoader] {modeId}: {warning}");
            foreach (var error in validation.Errors)
                BattleLuckPlugin.LogError($"[EventDefinitionLoader] {modeId}: {error}");
            return true;
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"Failed to load unified event '{modeId}': {ex.Message}");
            BattleLuckPlugin.LogError($"[EventDefinitionLoader] {validation.Errors[^1]}");
            return true;
        }
    }

    public ModeConfig LoadEffectiveConfig(string modeId)
    {
        if (!TryLoad(modeId, out var definition, out var validation))
            throw new FileNotFoundException($"Unified event config not found for mode '{modeId}' at '{Path.Combine(EventsRoot, $"{modeId}.json")}'.");

        if (definition == null || !validation.Success)
            throw new InvalidOperationException($"Unified event '{modeId}' failed validation. See logs for details.");

        if (!definition.Metadata.Enabled)
            throw new InvalidOperationException($"Unified event '{modeId}' is disabled.");

        return ProjectToModeConfig(modeId, definition);
    }

    public UnifiedEventDefinition? LoadRuntimeDefinition(string modeId)
    {
        if (!TryLoad(modeId, out var definition, out var validation))
            return null;

        if (definition == null || !validation.Success || !definition.Metadata.Enabled)
            return null;

        return definition;
    }

    ModeConfig ProjectToModeConfig(string modeId, UnifiedEventDefinition definition)
    {
        var displayName = !string.IsNullOrWhiteSpace(definition.Metadata.DisplayName)
            ? definition.Metadata.DisplayName
            : definition.Metadata.Id;
        var legacyZones = LoadLegacyZones(modeId);

        var config = new ModeConfig
        {
            ModeId = modeId,
            DisplayName = displayName,
            Description = string.Empty,
            Version = int.TryParse(definition.Metadata.Version, out var version) ? version : 1,
            KitId = modeId,
            UsesManagedPlayerLifecycle = true,
            KitConfig = KitController.LoadKit(modeId) ?? new KitConfig(),
            Session = new SessionConfig
            {
                Enabled = definition.Metadata.Enabled,
                DisplayName = displayName,
                Description = string.Empty,
                Rules = new SessionRules()
            },
            Rules = MapRules(definition.Rules, modeId, displayName),
            Zones = new ZonesConfig
            {
                Detection = legacyZones?.Detection ?? new DetectionConfig(),
                AutoEnter = legacyZones?.AutoEnter ?? new AutoEnterConfig(),
                Zones = definition.Zones
                    .Where(z => z.Hash != 0)
                    .Select(z => MergeZone(z, legacyZones?.Zones.FirstOrDefault(existing => existing.Hash == z.Hash)))
                    .ToList()
            }
        };

        ApplyRules(definition.Rules, config.Session.Rules);
        var prompt = new PromptContextLoader().Load(modeId);
        if (prompt != null || !string.IsNullOrWhiteSpace(definition.Metadata.Prompt))
        {
            config.EventPrompt = new EventPromptDefinition
            {
                EventId = modeId,
                AllowedActions = prompt?.AllowedActions ?? new List<string>(),
                BlockedActions = prompt?.BlockedActions ?? new List<string>(),
                AllowedTechs = prompt?.AllowedTechs ?? new List<string>(),
                Body = !string.IsNullOrWhiteSpace(prompt?.Narrative) ? prompt.Narrative : definition.Metadata.Prompt
            };

            if (prompt != null)
            {
                foreach (var zone in config.Zones.Zones)
                {
                    zone.AiRules ??= new ZoneAiRules();
                    zone.AiRules.AllowedActions = prompt.AllowedActions.ToList();
                    zone.AiRules.BlockedActions = prompt.BlockedActions.ToList();
                }
            }
        }
        return config;
    }

    static ZonesConfig? LoadLegacyZones(string modeId)
    {
        var path = Path.Combine(Path.Combine(ConfigLoader.ConfigRoot, "events"), modeId, "zones.json");
        if (!File.Exists(path))
        {
            // Fallback to legacy path
            path = Path.Combine(ConfigLoader.ConfigRoot, modeId, "zones.json");
            if (!File.Exists(path))
                return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ZonesConfig>(File.ReadAllText(path), ConfigLoader.JsonOptions);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[EventDefinitionLoader] Could not merge legacy zones for '{modeId}': {ex.Message}");
            return null;
        }
    }

    static RulesConfig MapRules(EventRulesDefinition rules, string modeId, string displayName)
    {
        var target = new RulesConfig
        {
            ModeId = modeId,
            DisplayName = displayName,
            MinPlayers = rules.MinPlayers ?? 1,
            MaxPlayers = rules.MaxPlayers ?? 4,
            EnablePvP = rules.EnablePvP ?? false,
            EnableVBloods = rules.EnableVBloods ?? false,
            EnableEliteMobs = rules.EnableEliteMobs ?? false,
            MatchDurationMinutes = rules.MatchDurationMinutes ?? 10,
            AllowLateJoin = rules.AllowLateJoin ?? false,
            EliminationMode = rules.EliminationMode ?? false,
            LivesPerPlayer = rules.LivesPerPlayer ?? 3,
            ZoneEnterRule = rules.ZoneEnterRule ?? "auto_enter",
            ActionStaging = rules.ActionStaging ?? new ActionStagingRules(),
            EventConsole = rules.EventConsole ?? new EventConsoleSettings()
        };

        if (rules.AdminTestMinPlayers.HasValue)
            target.AdminTestMinPlayers = Math.Max(1, rules.AdminTestMinPlayers.Value);
        if (rules.AllowAdminSoloTest.HasValue)
            target.AllowAdminSoloTest = rules.AllowAdminSoloTest.Value;
        if (rules.RequireReadyCheck.HasValue)
            target.RequireReadyCheck = rules.RequireReadyCheck.Value;
        if (rules.RestrictGear.HasValue)
            target.RestrictGear = rules.RestrictGear.Value;
        if (rules.ShareLoot.HasValue)
            target.ShareLoot = rules.ShareLoot.Value;
        if (rules.ResetOnExit.HasValue)
            target.ResetOnExit = rules.ResetOnExit.Value;

        return target;
    }

    public void Validate(UnifiedEventDefinition definition, EventValidationResult result, int maxActionsPerEvent = 1000)
    {
        if (string.IsNullOrWhiteSpace(definition.Metadata.Id))
            result.Errors.Add("metadata.id is required.");

        if (definition.ArenaRotation?.Enabled == true)
            ValidateArenaRotation(definition.ArenaRotation, result);

        var actionCount = CountActions(definition);
        if (actionCount > maxActionsPerEvent)
            result.Errors.Add($"Unified event contains {actionCount} actions; maximum allowed is {maxActionsPerEvent}.");

        var objectGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in definition.Objects)
        {
            if (string.IsNullOrWhiteSpace(obj.Group))
                result.Errors.Add("objects[].group is required.");
            else if (!objectGroups.Add(obj.Group))
                result.Errors.Add($"Duplicate object group '{obj.Group}'.");

            ValidateActions(obj.Actions, $"objects[{obj.Group}]", result);
        }

        foreach (var glow in definition.Glows)
            ValidateActions(glow.Actions, $"glows[{glow.Group}]", result);

        var zoneHashes = new HashSet<int>();
        for (var i = 0; i < definition.Zones.Count; i++)
        {
            var zone = definition.Zones[i];
            if (zone.Hash == 0)
                result.Errors.Add($"zones[{i}].hash must be a non-zero value.");
            else if (!zoneHashes.Add(zone.Hash))
                result.Errors.Add($"Duplicate zones[{i}].hash value {zone.Hash}. Each zone must use a unique hash.");
        }

        var phaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var phase in definition.Phases)
        {
            if (string.IsNullOrWhiteSpace(phase.Name))
                result.Errors.Add("phases[].name is required.");
            else if (!phaseNames.Add(phase.Name))
                result.Errors.Add($"Duplicate phases[].name '{phase.Name}'.");
            ValidateActions(phase.Actions, $"phases[{phase.Name}]", result);
        }

        foreach (var timer in definition.Timers)
        {
            if (string.IsNullOrWhiteSpace(timer.TimerId))
                result.Errors.Add("timers[].timerId is required.");
            if (timer.DurationSeconds <= 0)
                result.Errors.Add($"timers[{timer.TimerId}].durationSeconds must be greater than 0.");
            if (string.IsNullOrWhiteSpace(timer.StartPhase))
                timer.StartPhase = "active";
            ValidateActions(timer.OnCompleteActions, $"timers[{timer.TimerId}].onCompleteActions", result);
        }

        foreach (var trigger in definition.Triggers)
            ValidateTrigger(trigger, "triggers", result);

        ValidateActions(definition.Actions, "actions", result);
        ValidatePromptPolicy(definition, result);
    }

    void ValidatePromptPolicy(UnifiedEventDefinition definition, EventValidationResult result)
    {
        var modeId = definition.Metadata.Id;
        if (string.IsNullOrWhiteSpace(modeId))
            return;

        var prompt = new PromptContextLoader().Load(modeId);
        if (prompt == null)
            return;

        var registered = _actions.Entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in prompt.AllowedActions.Concat(prompt.BlockedActions).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!registered.Contains(name))
                result.Errors.Add($"prompt.txt references unknown action '{name}'.");
        }

        var blocked = prompt.BlockedActions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowed = prompt.AllowedActions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in EnumerateActionNames(definition))
        {
            if (blocked.Contains(name))
                result.Errors.Add($"Event action '{name}' is blocked by prompt.txt.");
            else if (allowed.Count > 0 && !allowed.Contains(name))
                result.Errors.Add($"Event action '{name}' is not allowed by prompt.txt.");
        }
    }

    static IEnumerable<string> EnumerateActionNames(UnifiedEventDefinition definition)
    {
        IEnumerable<EventActionDefinition> actions = definition.Actions
            .Concat(definition.Objects.SelectMany(o => o.Actions))
            .Concat(definition.Glows.SelectMany(g => g.Actions))
            .Concat(definition.Phases.SelectMany(p => p.Actions))
            .Concat(definition.Timers.SelectMany(t => t.OnCompleteActions))
            .Concat(definition.Triggers.SelectMany(t => t.Actions));

        return actions.Select(a => a.ToActionString().Split(':', 2)[0].Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a));
    }

    public static int CountActions(UnifiedEventDefinition definition)
    {
        var count = definition.Actions.Count;
        count += definition.Objects.Sum(o => o.Actions.Count);
        count += definition.Glows.Sum(g => g.Actions.Count);
        count += definition.Phases.Sum(p => p.Actions.Count);
        count += definition.Timers.Sum(t => t.OnCompleteActions.Count);
        count += definition.Triggers.Sum(t => t.Actions.Count);
        return count;
    }

    void ValidateTrigger(EventTriggerDefinition trigger, string scope, EventValidationResult result)
    {
        var name = EventTriggerRegistry.Normalize(trigger.Name);
        if (!EventTriggerRegistry.IsKnown(name))
            result.Errors.Add($"{scope}: unknown trigger '{trigger.Name}'.");
        trigger.Name = name;
        ValidateActions(trigger.Actions, $"{scope}[{trigger.Name}]", result);

        if (IsPerPlayerTrigger(name))
        {
            foreach (var action in trigger.Actions)
            {
                var actionName = action.ToActionString().Split(':', 2)[0].Trim();
                if (IsSessionGeometryAction(actionName))
                {
                    result.Errors.Add($"{scope}[{trigger.Name}]: geometry action '{actionName}' would replay for every player. Put it in an object or one-time setup/active phase.");
                }
            }
        }
    }

    static bool IsPerPlayerTrigger(string name) =>
        name is "battleluck.zone.enter" or "battleluck.player.connected" or
            "projectm.player.connected" or "projectm.player.respawned";

    static bool IsSessionGeometryAction(string actionName) =>
        actionName is "schematic.load" or "schematic.loadat" or "schematic.loadatpos" or
            "floor.place" or "wall.build" or "tile.place" or
            "zone.border.place" or "zone.border.place_all" or "platform.spawn";

    void ValidateActions(IEnumerable<EventActionDefinition> actions, string scope, EventValidationResult result)
    {
        foreach (var action in actions)
        {
            var validation = _actions.Validate(action);
            if (!validation.Success)
            {
                result.Errors.Add($"{scope}: {validation.Error}");
                continue;
            }

            var actionString = action.ToActionString();
            if (_customSequences.TryReadCustomSequenceAction(actionString, out var sequenceId, out _, out _))
            {
                if (string.IsNullOrWhiteSpace(sequenceId))
                {
                    result.Errors.Add($"{scope}: custom sequence action requires sequenceId/id/name.");
                    continue;
                }

                var get = _customSequences.Get(sequenceId);
                if (!get.Success)
                    result.Errors.Add($"{scope}: {get.Error}");
            }
        }
    }

    static void ApplyRules(EventRulesDefinition rules, SessionRules target)
    {
        if (rules.MinPlayers.HasValue) target.MinPlayers = Math.Max(1, rules.MinPlayers.Value);
        if (rules.AdminTestMinPlayers.HasValue) target.AdminTestMinPlayers = Math.Max(1, rules.AdminTestMinPlayers.Value);
        if (rules.AllowAdminSoloTest.HasValue) target.AllowAdminSoloTest = rules.AllowAdminSoloTest.Value;
        if (rules.MaxPlayers.HasValue) target.MaxPlayers = Math.Max(target.MinPlayers, rules.MaxPlayers.Value);
        if (rules.EnablePvP.HasValue) target.EnablePvP = rules.EnablePvP.Value;
        if (rules.EnableVBloods.HasValue) target.EnableVBloods = rules.EnableVBloods.Value;
        if (rules.EnableEliteMobs.HasValue) target.EnableEliteMobs = rules.EnableEliteMobs.Value;
        if (rules.MatchDurationMinutes.HasValue) target.MatchDurationMinutes = Math.Max(0, rules.MatchDurationMinutes.Value);
        if (rules.AllowLateJoin.HasValue) target.AllowLateJoin = rules.AllowLateJoin.Value;
        if (rules.RequireReadyCheck.HasValue) target.RequireReadyCheck = rules.RequireReadyCheck.Value;
        if (rules.RestrictGear.HasValue) target.RestrictGear = rules.RestrictGear.Value;
        if (rules.ShareLoot.HasValue) target.ShareLoot = rules.ShareLoot.Value;
        if (rules.ResetOnExit.HasValue) target.ResetOnExit = rules.ResetOnExit.Value;
        if (rules.EliminationMode.HasValue) target.EliminationMode = rules.EliminationMode.Value;
        if (rules.LivesPerPlayer.HasValue) target.LivesPerPlayer = Math.Max(0, rules.LivesPerPlayer.Value);
        if (rules.EventConsole != null) target.EventConsole = rules.EventConsole;
    }

    static ZoneDefinition MergeZone(EventZoneDefinition unified, ZoneDefinition? existing)
    {
        var zone = unified.ToZoneDefinition();
        if (existing == null)
            return zone;

        zone.Type = string.IsNullOrWhiteSpace(unified.Type) || unified.Type.Equals("arena", StringComparison.OrdinalIgnoreCase)
            ? existing.Type
            : unified.Type;
        zone.KitId = string.IsNullOrWhiteSpace(unified.KitId) ? existing.KitId : unified.KitId;
        zone.Waypoints = existing.Waypoints;
        zone.GlowBorder = existing.GlowBorder;
        zone.MovingPlatform = existing.MovingPlatform;
        zone.LootCrates = existing.LootCrates;
        zone.Glow = existing.Glow;
        zone.Bosses = existing.Bosses;

        if (unified.Boundary == null)
        {
            zone.Boundary = existing.Boundary;
            if (zone.Boundary != null && !string.IsNullOrWhiteSpace(unified.BoundaryPolicy))
                zone.Boundary.Policy = unified.BoundaryPolicy;
        }

        return zone;
    }

    static void ValidateArenaRotation(EventArenaRotationDefinition rotation, EventValidationResult result)
    {
        if (!rotation.Selection.Equals("round_robin", StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"arenaRotation.selection '{rotation.Selection}' is not supported; use round_robin.");

        if (rotation.Radius <= 0f)
            result.Errors.Add("arenaRotation.radius must be greater than 0.");

        if (rotation.ExitRadius > 0f && rotation.ExitRadius < rotation.Radius)
            result.Errors.Add("arenaRotation.exitRadius must be greater than or equal to radius.");

        if (rotation.Points.Count == 0)
            result.Errors.Add("arenaRotation.points must contain at least one point when rotation is enabled.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in rotation.Points)
        {
            if (string.IsNullOrWhiteSpace(point.Id))
                result.Errors.Add("arenaRotation.points[].id is required.");
            else if (!ids.Add(point.Id))
                result.Errors.Add($"Duplicate arenaRotation point id '{point.Id}'.");
        }
    }
}
