using System.Reflection;
using BattleLuck.Core;
using ProjectM.CastleBuilding;
using Unity.Transforms;
using UnityEngine;

public static class AdminCommands
{
    static readonly FieldInfo[] NetworkIdNumericFields = typeof(NetworkId)
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(field =>
            field.FieldType == typeof(byte) || field.FieldType == typeof(sbyte) ||
            field.FieldType == typeof(short) || field.FieldType == typeof(ushort) ||
            field.FieldType == typeof(int) || field.FieldType == typeof(uint) ||
            field.FieldType == typeof(long) || field.FieldType == typeof(ulong))
        .ToArray();

    [Command("merchant.list", description: "List merchant command item listings. Usage: .merchant.list", adminOnly: true)]
    public static void MerchantList(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
        {
            ctx.Reply("Merchant command service is not initialized.");
            return;
        }

        var listings = service.Listings.Where(l => l.Enabled).ToList();
        ctx.Reply($"Merchant listings: {listings.Count} enabled, active rentals={service.Rentals.Count}");
        foreach (var listing in listings.Take(12))
        {
            var item = listing.ItemGuid?.ToString(CultureInfo.InvariantCulture) ?? listing.ItemPrefab;
            ctx.Reply($"  #{listing.Number} {listing.Id}: {listing.DisplayName} kind={listing.Kind} item={item} adminOnly={listing.AdminOnly}");
        }
        if (listings.Count > 12)
            ctx.Reply($"  ... {listings.Count - 12} more.");
    }

    [Command("merchant.reload", description: "Reload config/BattleLuck/merchant_servant_actions.json", adminOnly: true)]
    public static void MerchantReload(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
        {
            ctx.Reply("Merchant command service is not initialized.");
            return;
        }

        service.Reload();
        ctx.Reply($"Merchant commands reloaded: {service.Listings.Count(l => l.Enabled)} enabled listing(s).");
    }

    [Command("merchant.give", description: "Give a merchant command token. Usage: .merchant.give <number|uuid|id> [playerNameOrSteamId=self] [amount=1]", adminOnly: true)]
    public static void MerchantGive(ChatCommandContext ctx, string listingId, string playerSelector = "self", int amount = 1)
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
        {
            ctx.Reply("Merchant command service is not initialized.");
            return;
        }

        if (!TryResolveAdminTarget(ctx, playerSelector, out var target, out var error))
        {
            ctx.Reply(error);
            return;
        }

        var result = service.GrantToken(target, listingId, amount);
        ctx.Reply(result.Success
            ? $"Granted merchant token '{listingId}' x{Math.Clamp(amount, 1, 999)} to {target.GetPlayerName()}."
            : result.UserMessage);
    }

    [Command("merchant.run", description: "Run a merchant command listing immediately. Usage: .merchant.run <number|uuid|id> [playerNameOrSteamId=self]", adminOnly: true)]
    public static void MerchantRun(ChatCommandContext ctx, string listingId, string playerSelector = "self")
    {
        var service = BattleLuckPlugin.MerchantCommands;
        if (service == null)
        {
            ctx.Reply("Merchant command service is not initialized.");
            return;
        }

        if (!TryResolveAdminTarget(ctx, playerSelector, out var target, out var error))
        {
            ctx.Reply(error);
            return;
        }

        if (!FlowController.TryGetUser(target, out var user))
        {
            ctx.Reply("Target user is not available.");
            return;
        }

        var result = service.ExecuteListing(target, user, listingId, consumeInventoryItem: false);
        ctx.Reply(result.Success
            ? $"Merchant listing '{listingId}' executed for {target.GetPlayerName()}."
            : result.UserMessage);
    }

    [Command("debugabilities", description: "Print all discovered AbilityGroup prefabs from the server", adminOnly: true)]
    public static void DebugAbilities(ChatCommandContext ctx)
    {
        try
        {
            PrefabHelper.ScanLivePrefabs();
            var abilityGroups = PrefabHelper.FindLive("AbilityGroup").ToList();

            ctx.Reply($"Found {abilityGroups.Count} AbilityGroup prefabs in PrefabCollectionSystem:");

            // Print in batches to avoid chat overflow
            int shown = 0;
            foreach (var kvp in abilityGroups.OrderBy(k => k.Key))
            {
                ctx.Reply($"  {kvp.Key} → {kvp.Value.GuidHash}");
                shown++;
                if (shown >= 50)
                {
                    ctx.Reply($"  ... and {abilityGroups.Count - shown} more. Use .exportprefabs for full list.");
                    break;
                }
            }

            // Also show combat key status
            var schools = AbilityController.AbilitiesBySchool;
            ctx.Reply($"Discovered schools: {string.Join(", ", schools.Where(s => s.Value.Count > 0).Select(s => $"{s.Key}({s.Value.Count})"))}");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Debug failed: {ex.Message}");
        }
    }

    [Command("debugslots", description: "Print combat key slot resolution status", adminOnly: true)]
    public static void DebugSlots(ChatCommandContext ctx)
    {
        try
        {
            PrefabHelper.ScanLivePrefabs();

            var checks = new (string Name, string PrefabName)[]
            {
                ("PrimaryAttack", "AB_Vampire_PrimaryAttack_AbilityGroup"),
                ("Dash",          "AB_Vampire_VampireDash_AbilityGroup"),
                ("VeilOfBlood",   "AB_Vampire_VeilOfBlood_AbilityGroup"),
            };

            foreach (var (name, prefabName) in checks)
            {
                var exact = PrefabHelper.GetPrefabGuidDeep(prefabName);
                var status = exact.HasValue ? $"OK → {exact.Value.GuidHash}" : "MISSING";
                ctx.Reply($"  {name}: {status} ({prefabName})");
            }

            // Show partial matches for Veil if exact failed
            var veilMatches = PrefabHelper.FindLive("Veil").Take(10).ToList();
            if (veilMatches.Count > 0)
            {
                ctx.Reply($"Veil partial matches ({veilMatches.Count}):");
                foreach (var m in veilMatches)
                    ctx.Reply($"  {m.Key} → {m.Value.GuidHash}");
            }
        }
        catch (Exception ex)
        {
            ctx.Reply($"Debug failed: {ex.Message}");
        }
    }

    [Command("reload", description: "Reload configs from disk", adminOnly: true)]
    public static void ReloadConfigs(ChatCommandContext ctx)
    {
        try
        {
            ConfigLoader.ReloadAll();
            SchematicLoader.LoadAll();
            ctx.Reply("Configs reloaded.");
        }
        catch (System.Exception ex)
        {
            ctx.Reply($"Failed to reload: {ex.Message}");
        }
    }

    [Command("schematic.capture", description: "Capture nearby castle design/items into config/BattleLuck/schematics. Usage: .schematic.capture <eventName> [radius] [description]", adminOnly: true)]
    public static void CaptureSchematic(ChatCommandContext ctx, string eventName, float radius = 60f, string description = "")
    {
        try
        {
            var player = ctx.Event.SenderCharacterEntity;
            if (!player.Exists())
            {
                ctx.Reply("Player entity not found.");
                return;
            }

            radius = Math.Clamp(radius, 5f, 250f);
            var center = player.GetPosition();
            var result = SchematicLoader.CaptureNearby(eventName, center, radius, description);
            if (!result.Success || result.Value == null)
            {
                ctx.Reply($"Schematic capture failed: {result.Error}");
                return;
            }

            var schematic = result.Value;
            ctx.Reply($"Schematic saved: {schematic.EventName}.json");
            ctx.Reply($"Captured structures={schematic.Structures.Count}, builtItems={schematic.BuiltItems.Count}, radius={radius:F0}.");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Schematic capture failed: {ex.Message}");
        }
    }

    [Command("schematic.load", description: "Load a schematic at your position. Usage: .schematic.load <eventName> [radius] [clearOld=true] [spawnItems=true] [scope=all|structures_only|items_only|world_map]", adminOnly: true)]
    public static void LoadSchematic(ChatCommandContext ctx, string eventName, float radius = 0f, bool clearOld = true, bool spawnItems = true, string scope = "")
    {
        try
        {
            var player = ctx.Event.SenderCharacterEntity;
            if (!player.Exists())
            {
                ctx.Reply("Player entity not found.");
                return;
            }

            radius = radius <= 0f ? 0f : Math.Clamp(radius, 5f, 500f);
            var scopeOverride = NullIfEmpty(scope);
            var result = SchematicLoader.LoadIntoWorld(eventName, player.GetPosition(), radius, clearOld, spawnItems,
                targetScopeOverride: scopeOverride);
            if (!result.Success || result.Value == null)
            {
                ctx.Reply($"Schematic load failed: {result.Error}");
                return;
            }

            var report = result.Value;
            ctx.Reply($"Schematic loaded: {report.EventName}");
            ctx.Reply($"Spawned structures={report.SpawnedStructures}, builtItems={report.SpawnedBuiltItems}, mapMarkers={report.SpawnedMapMarkers}, failed={report.FailedStructures + report.FailedBuiltItems}, destroyedOld={report.DestroyedOld}, radius={report.Radius:F0}.");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Schematic load failed: {ex.Message}");
        }
    }

    [Command("schematic.clear", description: "Clear tracked entities spawned by a schematic name. Usage: .schematic.clear <eventName>", adminOnly: true)]
    public static void ClearSchematic(ChatCommandContext ctx, string eventName)
    {
        var result = SchematicLoader.ClearByEventName(eventName);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Schematic clear failed: {result.Error}");
            return;
        }
        ctx.Reply($"Schematic cleared: {eventName} destroyed={result.Value.TotalDestroyed}.");
    }

    [Command("schematic.clear.radius", description: "Clear tracked schematic entities near you. Usage: .schematic.clear.radius <radius>", adminOnly: true)]
    public static void ClearSchematicRadius(ChatCommandContext ctx, float radius = 60f)
    {
        var player = ctx.Event.SenderCharacterEntity;
        if (!player.Exists())
        {
            ctx.Reply("Player entity not found.");
            return;
        }

        radius = Math.Clamp(radius, 5f, 500f);
        var result = SchematicLoader.ClearTrackedInRadius(player.GetPosition(), radius);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Schematic radius clear failed: {result.Error}");
            return;
        }
        ctx.Reply($"Tracked schematic radius clear: destroyed={result.Value.TotalDestroyed}, radius={radius:F0}.");
    }

    [Command("schematic.destroy.radius", description: "Clear BattleLuck-tracked schematic entities near you without touching world builds. Usage: .schematic.destroy.radius <radius>", adminOnly: true)]
    public static void DestroySchematicRadius(ChatCommandContext ctx, float radius = 60f, bool includeItems = true)
    {
        var player = ctx.Event.SenderCharacterEntity;
        if (!player.Exists())
        {
            ctx.Reply("Player entity not found.");
            return;
        }

        radius = Math.Clamp(radius, 5f, 500f);
        var result = SchematicLoader.DestroyWorldEntitiesInRadius(player.GetPosition(), radius, includeItems);
        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Schematic destroy radius failed: {result.Error}");
            return;
        }
        ctx.Reply($"Tracked schematic radius clear: destroyed={result.Value.DestroyedTracked}, world entities preserved, radius={radius:F0}.");
    }

    [Command("schematic.list", description: "List loaded BattleLuck schematics with their target scope", adminOnly: true)]
    public static void ListSchematics(ChatCommandContext ctx)
    {
        var names = SchematicLoader.GetAllEventNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            ctx.Reply("No schematics loaded. Use .schematic.capture <name> <radius> or add JSON under config/BattleLuck/schematics.");
            return;
        }

        ctx.Reply($"Loaded schematics ({names.Count}):");
        foreach (var name in names)
        {
            var schematic = SchematicLoader.GetSchematic(name);
            var scope = schematic?.TargetScope ?? "all";
            var markers = schematic?.MapMarkers.Count ?? 0;
            ctx.Reply($"  {name} [scope={scope}, markers={markers}]");
        }
    }

    [Command("sc.l", description: "Alias for .schematic.list", adminOnly: true)]
    public static void ListSchematicsAlias(ChatCommandContext ctx) => ListSchematics(ctx);

    [Command("event.schematic.export", description: "Export an event schematic as JSON. Usage: .event.schematic.export <id>", adminOnly: true)]
    public static void ExportSchematic(ChatCommandContext ctx, string id)
    {
        ctx.Reply($"Exporting schematic '{id}'...");
        // Logic handled by SchematicLoader
    }

    [Command("schematic.loadatpos", description: "Load a schematic at your position without cropping it. Usage: .schematic.loadatpos <eventName> [clearRadius=0] [heightOffset=0] [spawnItems=true] [scope=all|structures_only|items_only|world_map] [structureFilter=type1,type2]", adminOnly: true)]
    public static void LoadSchematicAtPosition(ChatCommandContext ctx, string eventName, float clearRadius = 0f, float heightOffset = 0f, bool spawnItems = true, string scope = "", string structureFilter = "")
    {
        var player = ctx.Event.SenderCharacterEntity;
        if (!player.Exists())
        {
            ctx.Reply("Player entity not found.");
            return;
        }

        var center = player.GetPosition() + new float3(0f, heightOffset, 0f);
        LoadSchematicAt(ctx, eventName, center, clearRadius, spawnItems, scope, structureFilter);
    }

    [Command("sc.lp", description: "Alias for .schematic.loadatpos", adminOnly: true)]
    public static void LoadSchematicAtPositionAlias(ChatCommandContext ctx, string eventName, float clearRadius = 0f, float heightOffset = 0f, bool spawnItems = true, string scope = "", string structureFilter = "")
        => LoadSchematicAtPosition(ctx, eventName, clearRadius, heightOffset, spawnItems, scope, structureFilter);

    [Command("schematic.loadat", description: "Load a schematic at coordinates without cropping it. Usage: .schematic.loadat <eventName> <x> <y> <z> [clearRadius=0] [spawnItems=true] [scope=all|structures_only|items_only|world_map] [structureFilter=type1,type2]", adminOnly: true)]
    public static void LoadSchematicAtCoordinates(ChatCommandContext ctx, string eventName, float x, float y, float z, float clearRadius = 0f, bool spawnItems = true, string scope = "", string structureFilter = "")
        => LoadSchematicAt(ctx, eventName, new float3(x, y, z), clearRadius, spawnItems, scope, structureFilter);

    [Command("sc.la", description: "Alias for .schematic.loadat", adminOnly: true)]
    public static void LoadSchematicAtCoordinatesAlias(ChatCommandContext ctx, string eventName, float x, float y, float z, float clearRadius = 0f, bool spawnItems = true, string scope = "", string structureFilter = "")
        => LoadSchematicAtCoordinates(ctx, eventName, x, y, z, clearRadius, spawnItems, scope, structureFilter);

    [Command("schematic.removeschematicrange", description: "Alias for .schematic.clear.radius", adminOnly: true)]
    public static void RemoveSchematicRange(ChatCommandContext ctx, float radius = 60f) => ClearSchematicRadius(ctx, radius);

    [Command("schematic.deleteallschematicentities", description: "Clear every tracked schematic/manual-build entity", adminOnly: true)]
    public static void DeleteAllSchematicEntities(ChatCommandContext ctx)
    {
        var result = SchematicLoader.ClearAllTracked();
        ctx.Reply(result.Success && result.Value != null
            ? $"All tracked schematic entities cleared: destroyed={result.Value.TotalDestroyed}."
            : $"Schematic clear-all failed: {result.Error}");
    }

    [Command("schematic.markers.clear", description: "Clear map markers for a schematic without removing structures. Usage: .schematic.markers.clear <eventName>", adminOnly: true)]
    public static void ClearSchematicMarkers(ChatCommandContext ctx, string eventName)
    {
        var zoneMap = BattleLuckPlugin.ZoneMap;
        if (zoneMap == null)
        {
            ctx.Reply("ZoneMapIconService not available.");
            return;
        }

        zoneMap.ClearSchematicMarkers(eventName);
        ctx.Reply($"Map markers cleared for schematic '{eventName}'.");
    }

    [Command("schematic.info", description: "Show details about a loaded schematic. Usage: .schematic.info <eventName>", adminOnly: true)]
    public static void SchematicInfo(ChatCommandContext ctx, string eventName)
    {
        var schematic = SchematicLoader.GetSchematic(eventName);
        if (schematic == null)
        {
            ctx.Reply($"Schematic '{eventName}' not found or disabled.");
            return;
        }

        ctx.Reply($"Schematic: {schematic.EventName}");
        ctx.Reply($"  scope={schematic.TargetScope}, structures={schematic.Structures.Count}, builtItems={schematic.BuiltItems.Count}, mapMarkers={schematic.MapMarkers.Count}");
        if (!string.IsNullOrWhiteSpace(schematic.Description))
            ctx.Reply($"  description: {schematic.Description}");
        if (schematic.MapMarkers.Count > 0)
            ctx.Reply($"  markers: {string.Join(", ", schematic.MapMarkers.Select(m => m.Label))}");
    }

    [Command("sc.i", description: "Alias for .schematic.info", adminOnly: true)]
    public static void SchematicInfoAlias(ChatCommandContext ctx, string eventName) => SchematicInfo(ctx, eventName);

    [Command("build.search", description: "Search live build/tile prefabs. Usage: .build.search <filter> [page]", adminOnly: true)]
    public static void BuildSearch(ChatCommandContext ctx, string filter, int page = 1)
    {
        var results = SearchBuildPrefabs(filter, page);
        if (results.Count == 0)
        {
            ctx.Reply($"No build prefabs matched '{filter}'.");
            return;
        }

        ctx.Reply($"Build prefabs for '{filter}' page {Math.Max(1, page)}:");
        foreach (var kv in results)
            ctx.Reply($"  <color=yellow>{kv.Key}</color> = {kv.Value.GuidHash}");
    }

    [Command("build.s", description: "Alias for .build.search", adminOnly: true)]
    public static void BuildSearchAlias(ChatCommandContext ctx, string filter, int page = 1) => BuildSearch(ctx, filter, page);

    [Command("build.check", description: "Show nearest build/tile prefab at cursor/player position. Usage: .build.check [radius]", adminOnly: true)]
    public static void BuildCheck(ChatCommandContext ctx, float radius = 5f)
    {
        var center = GetBuildCommandPosition(ctx);
        radius = Math.Clamp(radius, 1f, 50f);
        if (!TryFindNearestBuildEntity(center, radius, includeItems: true, out var entity, out var prefab, out var name, out var distance))
        {
            ctx.Reply($"No build/tile/item entity found within {radius:F1}.");
            return;
        }

        ctx.Reply($"Nearest build entity: <color=yellow>{name}</color> guid={prefab.GuidHash} entity={entity.Index} distance={distance:F1}");
    }

    [Command("palette.add", description: "Add the best matching live prefab to your build palette. Usage: .palette.add <searchterm>", adminOnly: true)]
    public static void PaletteAdd(ChatCommandContext ctx, string searchTerm)
    {
        var ownerId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var result = BuildPaletteService.Add(ownerId, searchTerm);
        ctx.Reply(result.Success && result.Value != null
            ? $"Palette added <color=green>{result.Value.Prefab}</color> = {result.Value.PrefabGuid}"
            : $"Palette add failed: {result.Error}");
    }

    [Command("pal.a", description: "Alias for .palette.add", adminOnly: true)]
    public static void PaletteAddAlias(ChatCommandContext ctx, string searchTerm) => PaletteAdd(ctx, searchTerm);

    [Command("palette.remove", description: "Remove entries from your build palette. Usage: .palette.remove <searchterm>", adminOnly: true)]
    public static void PaletteRemove(ChatCommandContext ctx, string searchTerm)
    {
        var ownerId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var result = BuildPaletteService.Remove(ownerId, searchTerm);
        ctx.Reply(result.Success ? "Palette entry removed." : $"Palette remove failed: {result.Error}");
    }

    [Command("pal.r", description: "Alias for .palette.remove", adminOnly: true)]
    public static void PaletteRemoveAlias(ChatCommandContext ctx, string searchTerm) => PaletteRemove(ctx, searchTerm);

    [Command("palette.list", description: "List your build palette", adminOnly: true)]
    public static void PaletteList(ChatCommandContext ctx)
    {
        var ownerId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var entries = BuildPaletteService.List(ownerId);
        if (entries.Count == 0)
        {
            ctx.Reply("Palette is empty. Use .palette.add <searchterm>.");
            return;
        }

        ctx.Reply($"Palette ({entries.Count}):");
        for (int i = 0; i < entries.Count; i++)
            ctx.Reply($"  {i + 1}. <color=yellow>{entries[i].Prefab}</color> = {entries[i].PrefabGuid}");
    }

    [Command("pal.l", description: "Alias for .palette.list", adminOnly: true)]
    public static void PaletteListAlias(ChatCommandContext ctx) => PaletteList(ctx);

    [Command("palette.clear", description: "Clear your build palette", adminOnly: true)]
    public static void PaletteClear(ChatCommandContext ctx)
    {
        var ownerId = ctx.Event.SenderCharacterEntity.GetSteamId();
        BuildPaletteService.Clear(ownerId);
        ctx.Reply("Palette cleared.");
    }

    [Command("pal.c", description: "Alias for .palette.clear", adminOnly: true)]
    public static void PaletteClearAlias(ChatCommandContext ctx) => PaletteClear(ctx);

    [Command("palette.next", description: "Cycle to the next build palette prefab", adminOnly: true)]
    public static void PaletteNext(ChatCommandContext ctx) => PaletteCycle(ctx, 1);

    [Command("palette.prev", description: "Cycle to the previous build palette prefab", adminOnly: true)]
    public static void PalettePrevious(ChatCommandContext ctx) => PaletteCycle(ctx, -1);

    [Command("pause", description: "Pause all active sessions", adminOnly: true)]
    public static void PauseSessions(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        session.PauseAll();
        ctx.Reply($"Paused {session.ActiveSessions.Count} active session(s).");
    }

    [Command("resume", description: "Resume paused sessions", adminOnly: true)]
    public static void ResumeSessions(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var count = session.ResumeAll();
        ctx.Reply($"Resumed {count} session(s).");
    }

    [Command("kick", description: "Kick player from session", adminOnly: true)]
    public static void KickPlayer(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            ctx.Reply("Invalid Steam ID.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        if (!session.TryKickPlayer(steamId, out var error))
        {
            ctx.Reply($"Kick failed: {error}");
            return;
        }

        ctx.Reply($"Kicked player {steamId}.");
    }

    [Command("setwinner", description: "Set winner and end session", adminOnly: true)]
    public static void SetWinner(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            ctx.Reply("Invalid Steam ID.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        if (!session.TrySetWinner(steamId, out var error))
        {
            ctx.Reply($"Set winner failed: {error}");
            return;
        }

        ctx.Reply($"Winner set to {steamId}. Session ended.");
    }

    [Command("zoneinfo", description: "Show zone stats and player counts", adminOnly: true)]
    public static void ZoneInfo(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var zones = session.ActiveSessions;
        if (zones.Count == 0)
        {
            ctx.Reply("No active zones.");
            return;
        }

        ctx.Reply($"Active zones ({zones.Count}):");
        foreach (var kv in zones)
        {
            var s = kv.Value;
            var state = s.IsStarted ? "running" : "waiting";
            ctx.Reply($"  Zone {kv.Key} — {s.Context.ModeId} ({s.Context.Players.Count} players, {state})");
        }
    }

    // ── Event Control Commands ──────────────────────────────────────────

    [Command("event.start", description: "Start an event mode (teleports you in). Add force=true only after reviewing high load.", adminOnly: true)]
    public static void EventStart(ChatCommandContext ctx, string modeId, bool force = false)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var registry = BattleLuckPlugin.GameModes;
        if (registry?.Resolve(modeId) == null) { ctx.Reply($"Unknown mode: {modeId}"); return; }

        var guard = OperatorSafetyService.CheckEventStart(modeId, force);
        if (!guard.Success || force)
        {
            EventDeploymentCommands.Audit.RecordGuard("event.start", modeId,
                guard.Success ? "START_WINDOW_FORCED" : "START_WINDOW_BLOCKED",
                guard.Success
                    ? "Explicit force accepted by admin."
                    : guard.Error ?? "Start blocked by operator safety guard.",
                forced: force);
        }
        if (!guard.Success)
        {
            ctx.Reply($"⚠️ {guard.Error}");
            ctx.Reply("Retry with `.event.start <mode> true` only after confirming the server is stable.");
            return;
        }

        var entity = ctx.Event.SenderCharacterEntity;
        session.ForceStart(modeId, entity);
        ctx.Reply($"Event '{modeId}' entered. Forced start is queued after build checks and the 10s stun countdown.");
    }

    [Command("event.end", description: "End all sessions for a mode and clear burning", adminOnly: true)]
    public static void EventEnd(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        session.ForceEndByModeId(modeId);
        ctx.Reply($"Event '{modeId}' ended. All sessions closed and burning cleared.");
    }

    [Command("event.endall", description: "End ALL active sessions and clear all burning", adminOnly: true)]
    public static void EventEndAll(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var modeIds = session.ActiveSessions.Values
            .Select(s => s.Context?.ModeId)
            .Where(m => m != null)
            .Distinct()
            .ToList();

        foreach (var modeId in modeIds)
            session.ForceEndByModeId(modeId!);

        ctx.Reply($"All events ended ({modeIds.Count} mode(s) stopped).");
    }

    [Command("event.status", description: "Show all active events and player counts", adminOnly: true)]
    public static void EventStatus(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var zones = session.ActiveSessions;
        if (zones.Count == 0)
        {
            ctx.Reply($"No active events. Burning: {session.BurningPlayerCount} player(s).");
            return;
        }

        ctx.Reply($"Active events ({zones.Count}):");
        foreach (var kv in zones)
        {
            var s = kv.Value;
            var state = s.IsStarted ? (s.IsPaused ? "paused" : "running") : "waiting";
            var elapsed = s.Context.ElapsedSeconds;
            var limit = s.Context.TimeLimitSeconds;
            ctx.Reply($"  {s.Context.ModeId} (zone {kv.Key}) — {s.Context.Players.Count} players, {state}, {elapsed:F0}/{limit}s");
        }
        ctx.Reply($"Entered: {session.EnteredPlayerCount} | Burning: {session.BurningPlayerCount}");
    }

    [Command("event.clearburning", description: "Remove burning penalty from all players", adminOnly: true)]
    public static void EventClearBurning(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var onlinePlayers = VRisingCore.GetOnlinePlayers();
        var cleared = session.ClearAllBurning(onlinePlayers);
        ctx.Reply($"Cleared burning from {cleared} player(s).");
    }

    [Command("event.forceenter", description: "Force a player into a mode. Usage: .event.forceenter <modeId> <steamId> [skipActions=true]", adminOnly: true)]
    public static void EventForceEnter(ChatCommandContext ctx, string modeId, string steamIdStr, bool skipActions = true)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId)) { ctx.Reply("Invalid Steam ID."); return; }

        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        // Find the player entity
        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.IsPlayer() && e.GetSteamId() == steamId);
        if (player == Entity.Null) { ctx.Reply($"Player {steamId} not found online."); return; }

        session.ForceStart(modeId, player, skipActions);
        ctx.Reply($"Force-entered player {steamId} into '{modeId}' and queued forced start (skipActions={skipActions}).");
    }

    [Command("event.forceexit", description: "Force a player out of their current event", adminOnly: true)]
    public static void EventForceExit(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId)) { ctx.Reply("Invalid Steam ID."); return; }

        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.IsPlayer() && e.GetSteamId() == steamId);
        if (player == Entity.Null) { ctx.Reply($"Player {steamId} not found online."); return; }

        if (!session.ForceExitPlayer(steamId, player))
        {
            ctx.Reply($"Player {steamId} is not in any active event.");
            return;
        }
        ctx.Reply($"Force-exited player {steamId}.");
    }

    // ── Auto-Trash Commands ─────────────────────────────────────────────

    [Command("autotrash", description: "Toggle auto-trash for dropped items in mode zones", adminOnly: true)]
    public static void AutoTrashToggle(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var trash = session.AutoTrash;
        trash.Enabled = !trash.Enabled;
        ctx.Reply($"Auto-trash is now {(trash.Enabled ? "ENABLED" : "DISABLED")}. Total items trashed: {trash.TotalTrashed}");
    }

    [Command("autotrash.status", description: "Show auto-trash stats", adminOnly: true)]
    public static void AutoTrashStatus(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var trash = session.AutoTrash;
        ctx.Reply($"Auto-trash: {(trash.Enabled ? "ON" : "OFF")} | Items destroyed: {trash.TotalTrashed}");
    }

    // ── Debug Spawn Commands ────────────────────────────────────────────

    [Command("spawntest", description: "Test-spawn a unit at your position. Usage: .spawntest <prefabGUID>", adminOnly: true)]
    public static void SpawnTest(ChatCommandContext ctx, int prefabHash)
    {
        var charEntity = ctx.Event.SenderCharacterEntity;
        var pos = charEntity.GetPosition();
        var prefab = new PrefabGUID(prefabHash);
        var spawnPos = pos + new Unity.Mathematics.float3(2f, 0f, 2f);

        var spawner = new SpawnController();
        spawner.SpawnWithCallback(prefab, spawnPos, duration: 0f, entity =>
        {
            var name = PrefabHelper.GetLivePrefabName(prefab) ?? prefab.GuidHash.ToString();
            BattleLuckPlugin.LogInfo($"[SpawnTest] Spawned {name} at ({pos.x:F0}, {pos.y:F0}, {pos.z:F0}). Entity: {entity.Index}");
        });

        var name2 = PrefabHelper.GetLivePrefabName(prefab) ?? prefabHash.ToString();
        ctx.Reply($"Spawn request sent for <color=green>{name2}</color> at ({spawnPos.x:F0}, {spawnPos.y:F0}, {spawnPos.z:F0}). Check logs for result.");
    }

    [Command("spawnwave", description: "Test-spawn a wave of enemies. Usage: .spawnwave <tier> <count>", adminOnly: true)]
    public static void SpawnWaveTest(ChatCommandContext ctx, int tier, int count)
    {
        var charEntity = ctx.Event.SenderCharacterEntity;
        var pos = charEntity.GetPosition();

        var enemies = SpawnController.GetEnemiesForWave(tier);
        var spawner = new SpawnController();

        spawner.SpawnWave(enemies, count, pos, spread: 5f, entities =>
        {
            BattleLuckPlugin.LogInfo($"[SpawnWaveTest] Wave complete: {entities.Count}/{count} tier-{tier} enemies.");
        });

        ctx.Reply($"Spawn wave request sent: {count} tier-{tier} enemies. Check logs for result.");
    }

    // ── Live Prefab Scan Commands ───────────────────────────────────────

    [Command("scanprefabs", description: "Scan live prefabs matching a filter. Usage: .scanprefabs <filter> [maxResults]", adminOnly: true)]
    public static void ScanPrefabs(ChatCommandContext ctx, string filter, int maxResults = 20)
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(maxResults).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} prefab(s) matching '{filter}':");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=yellow>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    [Command("scanbufs", description: "Scan live prefabs for buffs. Usage: .scanbufs [filter]", adminOnly: true)]
    public static void ScanBuffs(ChatCommandContext ctx, string filter = "Buff_General")
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(30).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} buff-related prefab(s):");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=cyan>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    [Command("scanitems", description: "Scan live prefabs for items. Usage: .scanitems <filter>", adminOnly: true)]
    public static void ScanItems(ChatCommandContext ctx, string filter = "Item_Weapon_Sword")
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(30).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} item prefab(s):");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=green>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    [Command("stashnpc", description: "Transfer items from NPC source(s) to destination entity. Usage: .stashnpc <destNetId> [sourceNetId|allteam] [maxDistance] [sameTeam] [minStack] [maxStacks] [itemFilter]", adminOnly: true)]
    public static void StashNpc(
        ChatCommandContext ctx,
        ulong destinationNetId,
        string sourceSelector = "allteam",
        float maxDistance = -1f,
        bool requireSameTeam = true,
        int minStack = 1,
        int maxStacks = 20,
        string itemFilter = "")
    {
        var sender = ctx.Event.SenderCharacterEntity;
        if (sender == Entity.Null || !sender.Exists())
        {
            ctx.Reply("Sender character is not available.");
            return;
        }

        if (!TryResolveEntityByNetworkNumeric(destinationNetId, out var destination))
        {
            ctx.Reply($"Destination network id {destinationNetId} was not found.");
            return;
        }

        if (!InventoryUtilities.TryGetInventoryEntity(VRisingCore.EntityManager, destination, out _))
        {
            ctx.Reply($"Destination {destinationNetId} does not expose an inventory entity.");
            return;
        }

        PrefabHelper.ScanLivePrefabs();

        Func<PrefabGUID, int, bool>? itemPredicate = null;
        if (!string.IsNullOrWhiteSpace(itemFilter))
        {
            itemPredicate = (prefab, _) =>
            {
                var name = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString();
                return name.Contains(itemFilter, StringComparison.OrdinalIgnoreCase);
            };
        }

        var options = new EntityExtensions.NpcStashTransferConditions
        {
            SourceMustBeNpc = true,
            RequireSameTeam = requireSameTeam,
            MaxDistance = maxDistance,
            MinStackAmount = Math.Max(1, minStack),
            MaxStacks = Math.Clamp(maxStacks, 1, 200),
            ItemFilter = itemPredicate
        };

        int moved;
        if (sourceSelector.Equals("allteam", StringComparison.OrdinalIgnoreCase))
        {
            var team = sender.Has<Team>() ? sender.Read<Team>().Value : int.MinValue;
            var candidates = GetNpcCandidatesForStash(destination, sender, requireSameTeam, team);

            moved = candidates.TryStashFromNpcs(destination, options);
            ctx.Reply($"stashnpc complete: moved {moved} item amount from {candidates.Count} NPC candidate(s) to destination {destinationNetId}.");
            return;
        }

        if (!ulong.TryParse(sourceSelector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceNetId))
        {
            ctx.Reply($"Invalid source selector '{sourceSelector}'. Use a network id or 'allteam'.");
            return;
        }

        if (!TryResolveEntityByNetworkNumeric(sourceNetId, out var sourceNpc))
        {
            ctx.Reply($"Source NPC network id {sourceNetId} was not found.");
            return;
        }

        moved = sourceNpc.TryStashFromNpc(destination, options);
        ctx.Reply($"stashnpc complete: moved {moved} item amount from source {sourceNetId} to destination {destinationNetId}.");
    }

    static bool TryResolveEntityByNetworkNumeric(ulong networkIdValue, out Entity resolved)
    {
        resolved = Entity.Null;
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        var entities = query.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var networkId = entity.Read<NetworkId>();
                if (!TryExtractNetworkIdKeys(networkId, out var keys))
                    continue;

                if (keys.Contains(networkIdValue))
                {
                    resolved = entity;
                    return true;
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return false;
    }

    static List<Entity> GetNpcCandidatesForStash(Entity destination, Entity sender, bool requireSameTeam, int senderTeam)
    {
        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
        var entities = query.ToEntityArray(Allocator.Temp);
        var candidates = new List<Entity>();

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists() || entity == destination || entity == sender)
                    continue;

                if (entity.Has<PlayerCharacter>())
                    continue;

                if (requireSameTeam && (!entity.Has<Team>() || entity.Read<Team>().Value != senderTeam))
                    continue;

                candidates.Add(entity);
            }
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
            query.Dispose();
        }

        return candidates;
    }

    static bool TryExtractNetworkIdKeys(NetworkId networkId, out HashSet<ulong> keys)
    {
        keys = new HashSet<ulong>();

        var directText = networkId.ToString();
        if (ulong.TryParse(directText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
            keys.Add(direct);

        var digitOnly = new string(directText.Where(char.IsDigit).ToArray());
        if (ulong.TryParse(digitOnly, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
            keys.Add(digits);

        object boxed = networkId;
        foreach (var field in NetworkIdNumericFields)
        {
            var value = field.GetValue(boxed);
            if (value == null)
                continue;

            switch (value)
            {
                case byte b:
                    keys.Add(b);
                    break;
                case sbyte sb when sb >= 0:
                    keys.Add((ulong)sb);
                    break;
                case short s when s >= 0:
                    keys.Add((ulong)s);
                    break;
                case ushort us:
                    keys.Add(us);
                    break;
                case int i when i >= 0:
                    keys.Add((ulong)i);
                    break;
                case uint ui:
                    keys.Add(ui);
                    break;
                case long l when l >= 0:
                    keys.Add((ulong)l);
                    break;
                case ulong ul:
                    keys.Add(ul);
                    break;
            }
        }

        return keys.Count > 0;
    }

    // ── AI Assistant Admin Commands ─────────────────────────────────────

    [Command("aiadmin", description: "AI administration: diagnostics | reload | recover | permissions", adminOnly: true)]
    public static async Task AIAdmin(ChatCommandContext ctx, string operation = "diagnostics")
    {
        switch ((operation ?? "").Trim().ToLowerInvariant())
        {
            case "diagnostics":
                await AIAdminStatus(ctx);
                break;
            case "reload":
            case "recover":
                AIReload(ctx);
                break;
            case "permissions":
                ctx.Reply("AI permissions: .ai chat is available to all players; action approval, reload, recovery, persistence, and diagnostics are admin/director-only.");
                break;
            default:
                ctx.Reply("Usage: .aiadmin diagnostics | reload | recover | permissions");
                break;
        }
    }

    [Command("ai.status", description: "Show detailed AI assistant status", adminOnly: true)]
    public static async Task AIAdminStatus(ChatCommandContext ctx)
    {
        try
        {
            var aiAssistant = BattleLuckPlugin.AIAssistant;
            if (aiAssistant == null)
            {
                ctx.Reply("AI Assistant is not initialized.");
                return;
            }

            var config = ConfigLoader.LoadAIConfig();
            var status = aiAssistant.IsEnabled ? "ENABLED" : "DISABLED";
            
            ctx.Reply($"🤖 AI Assistant Status: {status}");
            ctx.Reply($"Configuration: {(config.Enabled ? "Enabled" : "Disabled")}");
            ctx.Reply($"Provider: {config.Provider}");
            var llamaOnly = config.Provider.Equals("llama", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("llama_api", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("meta_llama", StringComparison.OrdinalIgnoreCase);
            var cloudflareProvider = config.Provider.Equals("cloudflare", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("cloudflare_ai", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("workers_ai", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("workers-ai", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("qwen", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("cloudflare_qwen", StringComparison.OrdinalIgnoreCase) ||
                config.Provider.Equals("qwen_cloudflare", StringComparison.OrdinalIgnoreCase);
            if (llamaOnly)
            {
                ctx.Reply($"Llama endpoint: {config.LlamaAPI.BaseUrl}");
                ctx.Reply($"Llama API Key: {(!string.IsNullOrWhiteSpace(config.LlamaAPI.ApiKey) ? "Configured" : "Not required for local endpoint")}");
                ctx.Reply("Google/Cloudflare: disabled for Llama-only mode");
                ctx.Reply($"Model: {config.LlamaAPI.Model}");
            }
            else if (cloudflareProvider)
            {
                ctx.Reply($"Cloudflare AI Auth Token: {(!string.IsNullOrWhiteSpace(config.CloudflareAI.ApiToken) ? "Configured" : "Not set")}");
                ctx.Reply($"Model: {config.CloudflareAI.Model}");
            }
            else
            {
                ctx.Reply($"Google AI Studio API Key: {(!string.IsNullOrEmpty(config.GoogleAIStudio.ApiKey) ? "Configured" : "Not set")}");
                ctx.Reply($"Model: {config.GoogleAIStudio.Model}");
            }
            ctx.Reply($"Provider status: {aiAssistant.ProviderStatus}");
            ctx.Reply($"Auto Tips: {(config.Messaging.AutoTipsEnabled ? "ON" : "OFF")}");
            ctx.Reply($"Message Cooldown: {config.Messaging.MessageCooldownSeconds}s");
            ctx.Reply($"Battle Sidecar: {(config.Sidecar.Enabled ? "ON" : "OFF")}");

            if (config.Sidecar.Enabled)
            {
                ctx.Reply($"Sidecar URL: {config.Sidecar.BaseUrl}");
                var health = await aiAssistant.GetSidecarHealthAsync();
                if (health != null)
                {
                    ctx.Reply($"Sidecar Health: {health.Status} ({health.Version})");
                }
                else if (!string.IsNullOrWhiteSpace(aiAssistant.SidecarLastError))
                {
                    ctx.Reply($"Sidecar Health: UNAVAILABLE ({aiAssistant.SidecarLastError})");
                }
                else
                {
                    ctx.Reply("Sidecar Health: UNAVAILABLE");
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI status command failed: {ex.Message}");
            ctx.Reply($"AI status failed: {ex.Message}");
        }
    }

    [Command("ai.reload", description: "Reload AI configuration and restart service", adminOnly: true)]
    public static void AIReload(ChatCommandContext ctx)
    {
        try
        {
            // Shutdown existing AI
            var currentAI = BattleLuckPlugin.AIAssistant;
            currentAI?.Shutdown();

            // Reload config
            ConfigLoader.ReloadAIConfig();
            var aiConfig = ConfigLoader.LoadAIConfig();

            if (aiConfig.Enabled)
            {
                // Reinitialize AI
                var newAI = new BattleLuck.Core.AIAssistant();
                newAI.Initialize(aiConfig);
                
                BattleLuckPlugin.SetAIAssistant(newAI);

                ctx.Reply($"AI Assistant reloaded successfully. Provider status: {newAI.ProviderStatus}");
            }
            else
            {
                BattleLuckPlugin.SetAIAssistant(null);
                    
                ctx.Reply("AI Assistant disabled (check configuration).");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"AI reload failed: {ex.Message}");
            ctx.Reply($"AI reload failed: {ex.Message}");
        }
    }

    [Command("ai.test", description: "Test AI assistant with a sample query", adminOnly: true)]
    public static async Task AITest(ChatCommandContext ctx, string query = "Hello, can you help players?")
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized for testing.");
            return;
        }

        try
        {
            var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
            var response = await aiAssistant.HandleDirectQuery(steamId, query, source: "admin");
            
            ctx.Reply($"🤖 Test Query: {query}");
            ctx.Reply(aiAssistant.FormatInGameResponse(query, response));
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"AI test failed: {ex.Message}");
            ctx.Reply($"AI test failed: {ex.Message}");
        }
    }

    [Command("ai.event", description: "Replay AI event flow (start, score, elimination, end) without entering a zone", adminOnly: true)]
    public static void AIEventReplay(ChatCommandContext ctx, string modeId = "aievent")
    {
        try
        {
            GameModeContext? replayContext = null;
            var sessionController = BattleLuckPlugin.Session;
            if (sessionController != null)
            {
                replayContext = sessionController.ActiveSessions.Values
                    .Select(s => s.Context)
                    .FirstOrDefault(c => c != null && c.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase));
            }

            if (replayContext == null)
            {
                var senderSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
                replayContext = new GameModeContext
                {
                    SessionId = $"admin-{modeId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                    ZoneHash = -1,
                    ModeId = modeId,
                    Broadcast = msg => ctx.Reply(msg)
                };

                replayContext.Players.Add(senderSteamId);

                var secondPlayer = VRisingCore.GetOnlinePlayers()
                    .Where(e => e.IsPlayer())
                    .Select(e => e.GetSteamId())
                    .FirstOrDefault(id => id != senderSteamId);

                if (secondPlayer != 0)
                    replayContext.Players.Add(secondPlayer);

                ctx.Reply($"No active '{modeId}' session found. Replaying with synthetic context ({replayContext.Players.Count} player(s)).");
            }
            else
            {
                ctx.Reply($"Replaying AI flow in active session {replayContext.SessionId} ({replayContext.ModeId}).");
            }

            GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
            {
                SessionId = replayContext.SessionId,
                ModeId = replayContext.ModeId,
            });

            // Note: AiEventMode.EmitCoreAiTestSequence was removed; test flows use session.json declarative config.
            var leaderboard = replayContext.Scores.GetLeaderboard();
            var topPlayer = leaderboard.Count > 0 ? leaderboard[0] : 0UL;
            GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
            {
                SessionId = replayContext.SessionId,
                ModeId = replayContext.ModeId,
                WinnerSteamId = topPlayer != 0 ? topPlayer : null
            });

            ctx.Reply("AI event flow emitted: mode.start -> player.scored -> player.eliminated -> mode.end");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"ai.event failed: {ex.Message}");
            ctx.Reply($"ai.event failed: {ex.Message}");
        }
    }

    static void LoadSchematicAt(ChatCommandContext ctx, string eventName, float3 center, float clearRadius, bool spawnItems, string scope = "", string structureFilter = "")
    {
        clearRadius = clearRadius <= 0f ? 0f : Math.Clamp(clearRadius, 1f, 500f);
        var scopeOverride = NullIfEmpty(scope);
        IReadOnlyList<string>? filter = null;
        if (!string.IsNullOrWhiteSpace(structureFilter))
            filter = structureFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = SchematicLoader.LoadIntoWorld(
            eventName,
            center,
            radius: 0f,
            clearOld: true,
            spawnBuiltItems: spawnItems,
            clearRadius: clearRadius,
            targetScopeOverride: scopeOverride,
            structureFilter: filter);

        if (!result.Success || result.Value == null)
        {
            ctx.Reply($"Schematic load-at failed: {result.Error}");
            return;
        }

        var report = result.Value;
        ctx.Reply($"Schematic loaded at ({center.x:F1},{center.y:F1},{center.z:F1}): {report.EventName}");
        ctx.Reply($"Spawned structures={report.SpawnedStructures}, builtItems={report.SpawnedBuiltItems}, mapMarkers={report.SpawnedMapMarkers}, failed={report.FailedStructures + report.FailedBuiltItems}, cleared={report.DestroyedOld}.");
    }

    static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static List<KeyValuePair<string, PrefabGUID>> SearchBuildPrefabs(string filter, int page)
    {
        PrefabHelper.ScanLivePrefabs();
        page = Math.Max(1, page);
        const int pageSize = 20;

        return PrefabHelper.FindLive(filter)
            .Where(kv => LooksLikeBuildPrefab(kv.Key))
            .OrderByDescending(kv => BuildPrefabScore(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    static string? ResolvePalettePrefab(ulong ownerId, string prefab)
    {
        if (!prefab.Equals("palette", StringComparison.OrdinalIgnoreCase) &&
            !prefab.Equals("current", StringComparison.OrdinalIgnoreCase))
            return prefab;

        var current = BuildPaletteService.Current(ownerId);
        return current.Success ? current.Value?.Prefab : null;
    }

    static void PaletteCycle(ChatCommandContext ctx, int delta)
    {
        var ownerId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var result = BuildPaletteService.Cycle(ownerId, delta);
        ctx.Reply(result.Success && result.Value != null
            ? $"Palette selected <color=green>{result.Value.Prefab}</color> = {result.Value.PrefabGuid}"
            : $"Palette cycle failed: {result.Error}");
    }

    static float3 GetBuildCommandPosition(ChatCommandContext ctx)
    {
        if (TryGetCommandWorldPosition(ctx, out var cursor, out _))
            return cursor;

        var player = ctx.Event.SenderCharacterEntity;
        return player.Exists() ? player.GetPosition() : float3.zero;
    }

    static bool TryFindNearestBuildEntity(
        float3 center,
        float radius,
        bool includeItems,
        out Entity entity,
        out PrefabGUID prefab,
        out string prefabName,
        out float distance)
    {
        entity = Entity.Null;
        prefab = PrefabGUID.Empty;
        prefabName = "";
        distance = float.MaxValue;

        if (!VRisingCore.IsReady)
            return false;

        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Translation>() },
            Any = includeItems
                ? new[] { ComponentType.ReadOnly<EditableTileModel>(), ComponentType.ReadOnly<TilePosition>(), ComponentType.ReadOnly<ItemPickup>() }
                : new[] { ComponentType.ReadOnly<EditableTileModel>(), ComponentType.ReadOnly<TilePosition>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        var arr = query.ToEntityArray(Allocator.Temp);
        try
        {
            var radiusSq = radius * radius;
            for (var i = 0; i < arr.Length; i++)
            {
                var candidate = arr[i];
                if (!em.Exists(candidate) || candidate.Has<PlayerCharacter>())
                    continue;

                var pos = em.GetComponentData<Translation>(candidate).Value;
                var dx = pos.x - center.x;
                var dz = pos.z - center.z;
                var distSq = dx * dx + dz * dz;
                if (distSq > radiusSq || distSq >= distance * distance)
                    continue;

                entity = candidate;
                distance = math.sqrt(distSq);
            }
        }
        finally
        {
            arr.Dispose();
            query.Dispose();
        }

        if (!entity.Exists())
            return false;

        prefab = entity.Has<PrefabGUID>() ? entity.Read<PrefabGUID>() : PrefabGUID.Empty;
        prefabName = PrefabHelper.GetLivePrefabName(prefab) ?? PrefabHelper.GetName(prefab) ?? prefab.GuidHash.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    static bool TryGetCommandWorldPosition(ChatCommandContext ctx, out float3 position, out string error)
    {
        position = float3.zero;
        error = "";

        var evt = ctx.Event;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var members = evt.GetType()
            .GetMembers(flags)
            .Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property)
            .Where(m =>
            {
                var name = m.Name;
                if (name.Contains("Sender", StringComparison.OrdinalIgnoreCase))
                    return false;
                return name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Cursor", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Aim", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("World", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Position", StringComparison.OrdinalIgnoreCase);
            });

        foreach (var member in members)
        {
            object? value;
            try
            {
                value = member switch
                {
                    FieldInfo field => field.GetValue(evt),
                    PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(evt),
                    _ => null
                };
            }
            catch
            {
                continue;
            }

            if (TryExtractFloat3(value, out position))
                return true;
        }

        error = "This command event did not expose a mouse/cursor world position; using player position fallback.";
        return false;
    }

    static bool TryExtractFloat3(object? value, out float3 position)
    {
        position = float3.zero;
        if (value == null)
            return false;
        if (value is float3 f3)
        {
            position = f3;
            return true;
        }
        if (value is Vector3 v3)
        {
            position = new float3(v3.x, v3.y, v3.z);
            return true;
        }

        var type = value.GetType();
        if (TryReadFloatMember(value, type, "x", out var x) &&
            TryReadFloatMember(value, type, "y", out var y) &&
            TryReadFloatMember(value, type, "z", out var z))
        {
            position = new float3(x, y, z);
            return true;
        }
        if (TryReadFloatMember(value, type, "X", out x) &&
            TryReadFloatMember(value, type, "Y", out y) &&
            TryReadFloatMember(value, type, "Z", out z))
        {
            position = new float3(x, y, z);
            return true;
        }

        return false;
    }

    static bool TryReadFloatMember(object value, Type type, string name, out float result)
    {
        result = 0f;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        try
        {
            var field = type.GetField(name, flags);
            if (field?.GetValue(value) is { } fv && TryConvertFloat(fv, out result))
                return true;
            var property = type.GetProperty(name, flags);
            if (property?.GetIndexParameters().Length == 0 && property.GetValue(value) is { } pv && TryConvertFloat(pv, out result))
                return true;
        }
        catch { }
        return false;
    }

    static bool TryConvertFloat(object value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            default:
                return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    static bool LooksLikeBuildPrefab(string prefabName) =>
        prefabName.Contains("TM_", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Castle", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
        prefabName.Contains("Carpet", StringComparison.OrdinalIgnoreCase);

    static int BuildPrefabScore(string prefabName)
    {
        var score = 0;
        if (prefabName.Contains("TM_Castle", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase)) score += 18;
        if (prefabName.Contains("Carpet", StringComparison.OrdinalIgnoreCase)) score += 16;
        if (prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase)) score += 15;
        return score;
    }

    static string ClassifyBuildKind(string prefabName)
    {
        if (prefabName.Contains("Floor", StringComparison.OrdinalIgnoreCase)) return "floor";
        if (prefabName.Contains("Wall", StringComparison.OrdinalIgnoreCase)) return "wall";
        if (prefabName.Contains("Door", StringComparison.OrdinalIgnoreCase)) return "door";
        if (prefabName.Contains("Gate", StringComparison.OrdinalIgnoreCase)) return "gate";
        if (prefabName.Contains("Carpet", StringComparison.OrdinalIgnoreCase)) return "carpet";
        if (prefabName.Contains("Tile", StringComparison.OrdinalIgnoreCase)) return "tile";
        return "prefab";
    }

    static bool TryResolveAdminTarget(ChatCommandContext ctx, string selector, out Entity player, out string error)
    {
        player = Entity.Null;
        error = "";

        if (string.IsNullOrWhiteSpace(selector) || selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            player = ctx.Event.SenderCharacterEntity;
            if (player.Exists())
                return true;
            error = "Sender character is not available.";
            return false;
        }

        foreach (var candidate in VRisingCore.GetOnlinePlayers())
        {
            if (!candidate.Exists() || !candidate.IsPlayer())
                continue;

            var steamId = candidate.GetSteamId();
            if (ulong.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSteamId) &&
                steamId == parsedSteamId)
            {
                player = candidate;
                return true;
            }

            var name = candidate.GetPlayerName();
            if (name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(selector, StringComparison.OrdinalIgnoreCase))
            {
                player = candidate;
                return true;
            }
        }

        error = $"No online player matched '{selector}'.";
        return false;
    }
}
