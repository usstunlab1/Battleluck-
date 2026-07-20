using ProjectM.Shared;
using Unity.Entities;

namespace BattleLuck.Services;

/// <summary>
/// Tracks equipment durability plus event-created/discovered entities and their
/// changes for the lifetime of one player event run.
///
/// Tracking starts once on the first committed event entry and continues across
/// every zone, stage, weapon replacement, and loadout change. It is cleared only
/// on final event exit or shutdown.
/// </summary>
public sealed class PlayerEquipmentTrackingService
{
    readonly Dictionary<ulong, TrackedPlayerEquipment> _tracked = new();

    public void StartTrackingPlayer(Entity player) =>
        StartTrackingEvent(player, eventRunId: "", eventId: "");

    public bool StartTrackingEvent(Entity player, string eventRunId, string eventId)
    {
        if (!player.Exists() || !player.Has<Equipment>())
            return false;

        var steamId = player.GetSteamId();
        if (steamId == 0)
            return false;

        if (_tracked.TryGetValue(steamId, out var existing))
        {
            // First entry owns the tracking run. Later zones must not reset the
            // baseline or discard already observed entity changes.
            if (string.IsNullOrWhiteSpace(existing.EventRunId) && !string.IsNullOrWhiteSpace(eventRunId))
            {
                existing.EventRunId = eventRunId.Trim();
                existing.EventId = eventId?.Trim() ?? "";
            }
            return false;
        }

        var tracked = TrackedPlayerEquipment.Capture(player);
        tracked.EventRunId = eventRunId?.Trim() ?? "";
        tracked.EventId = eventId?.Trim() ?? "";
        tracked.ApplyNoDurability();
        _tracked[steamId] = tracked;

        BattleLuckPlugin.LogInfo(
            $"[EquipmentTrack] Started tracking for {steamId}; event={tracked.EventId}, run={tracked.EventRunId}, baselineEntities={tracked.BaselineEntityCount}.");
        return true;
    }

    public bool IsTracking(ulong steamId) => _tracked.ContainsKey(steamId);

    public string GetEventRunId(ulong steamId) =>
        _tracked.TryGetValue(steamId, out var tracked) ? tracked.EventRunId : "";

    public void TrackCreatedEntity(ulong steamId, Entity entity, string sourceAction = "", string stageId = "")
    {
        if (!_tracked.TryGetValue(steamId, out var tracked) || !entity.Exists())
            return;

        tracked.Record(entity, EventEntityChangeKind.Created, sourceAction, stageId, createdByEvent: true);
    }

    public void TrackChangedEntity(ulong steamId, Entity entity, string sourceAction = "", string stageId = "")
    {
        if (!_tracked.TryGetValue(steamId, out var tracked) || !entity.Exists())
            return;

        tracked.Record(entity, EventEntityChangeKind.Modified, sourceAction, stageId);
    }

    public void TrackDestroyedEntity(ulong steamId, Entity entity, string sourceAction = "", string stageId = "")
    {
        if (!_tracked.TryGetValue(steamId, out var tracked) || entity == Entity.Null)
            return;

        tracked.Record(entity, EventEntityChangeKind.Destroyed, sourceAction, stageId);
    }

    public IReadOnlyList<EventEntityChangeRecord> GetTrackedChanges(ulong steamId)
    {
        if (!_tracked.TryGetValue(steamId, out var tracked))
            return Array.Empty<EventEntityChangeRecord>();

        return tracked.Changes.Values
            .OrderBy(change => change.FirstObservedUtc)
            .Select(change => change.Clone())
            .ToList();
    }

    public EventTrackingSummary GetSummary(ulong steamId)
    {
        if (!_tracked.TryGetValue(steamId, out var tracked))
            return new EventTrackingSummary { SteamId = steamId };

        return new EventTrackingSummary
        {
            SteamId = steamId,
            EventId = tracked.EventId,
            EventRunId = tracked.EventRunId,
            BaselineEntityCount = tracked.BaselineEntityCount,
            TrackedEntityCount = tracked.Changes.Count,
            CreatedCount = tracked.Changes.Values.Count(change => change.CreatedByEvent),
            ModifiedCount = tracked.Changes.Values.Count(change => change.Modified),
            DestroyedCount = tracked.Changes.Values.Count(change => change.Destroyed)
        };
    }

    public void StopTrackingPlayer(ulong steamId)
    {
        if (!_tracked.Remove(steamId, out var tracked))
            return;

        tracked.Restore();
        var summary = tracked.CreateSummary(steamId);
        BattleLuckPlugin.LogInfo(
            $"[EquipmentTrack] Stopped tracking for {steamId}; event={summary.EventId}, run={summary.EventRunId}, tracked={summary.TrackedEntityCount}, created={summary.CreatedCount}, modified={summary.ModifiedCount}, destroyed={summary.DestroyedCount}.");
    }

    public void StopTrackingEvent(ulong steamId) => StopTrackingPlayer(steamId);

    public void StopTrackingPlayer(Entity player) => StopTrackingPlayer(player.GetSteamId());

    public void Tick()
    {
        foreach (var steamId in _tracked.Keys.ToList())
        {
            var tracked = _tracked[steamId];
            if (!tracked.Player.Exists() || !tracked.Player.Has<Equipment>())
            {
                StopTrackingPlayer(steamId);
                continue;
            }

            tracked.RefreshChangedSlots();
        }
    }

    public void RestoreAll()
    {
        foreach (var tracked in _tracked.Values)
            tracked.Restore();
        _tracked.Clear();
    }

    sealed class TrackedPlayerEquipment
    {
        public Entity Player;
        public string EventRunId = "";
        public string EventId = "";
        public TrackedItem Head;
        public TrackedItem Chest;
        public TrackedItem Gloves;
        public TrackedItem Legs;
        public TrackedItem Boots;
        public TrackedItem Cloak;
        public TrackedItem Weapon;
        public TrackedItem Grimoire;
        public TrackedItem Bag;
        public HashSet<EntityKey> BaselineEntities { get; } = new();
        public Dictionary<EntityKey, EventEntityChangeRecord> Changes { get; } = new();

        public int BaselineEntityCount => BaselineEntities.Count;

        public static TrackedPlayerEquipment Capture(Entity player)
        {
            var equipment = player.Read<Equipment>();
            var tracked = new TrackedPlayerEquipment
            {
                Player = player,
                Head = TrackedItem.FromSlot(equipment.ArmorHeadgearSlot),
                Chest = TrackedItem.FromSlot(equipment.ArmorChestSlot),
                Gloves = TrackedItem.FromSlot(equipment.ArmorGlovesSlot),
                Legs = TrackedItem.FromSlot(equipment.ArmorLegsSlot),
                Boots = TrackedItem.FromSlot(equipment.ArmorFootgearSlot),
                Cloak = TrackedItem.FromSlot(equipment.CloakSlot),
                Weapon = TrackedItem.FromSlot(equipment.WeaponSlot),
                Grimoire = TrackedItem.FromSlot(equipment.GrimoireSlot),
                Bag = TrackedItem.FromSlot(equipment.BagSlot)
            };

            tracked.RememberBaseline(tracked.Head.Item);
            tracked.RememberBaseline(tracked.Chest.Item);
            tracked.RememberBaseline(tracked.Gloves.Item);
            tracked.RememberBaseline(tracked.Legs.Item);
            tracked.RememberBaseline(tracked.Boots.Item);
            tracked.RememberBaseline(tracked.Cloak.Item);
            tracked.RememberBaseline(tracked.Weapon.Item);
            tracked.RememberBaseline(tracked.Grimoire.Item);
            tracked.RememberBaseline(tracked.Bag.Item);
            return tracked;
        }

        void RememberBaseline(Entity entity)
        {
            if (entity != Entity.Null)
                BaselineEntities.Add(EntityKey.From(entity));
        }

        public void ApplyNoDurability()
        {
            Head.SetNoDurability();
            Chest.SetNoDurability();
            Gloves.SetNoDurability();
            Legs.SetNoDurability();
            Boots.SetNoDurability();
            Cloak.SetNoDurability();
            Weapon.SetNoDurability();
            Grimoire.SetNoDurability();
            Bag.SetNoDurability();
        }

        public void Restore()
        {
            Head.Restore();
            Chest.Restore();
            Gloves.Restore();
            Legs.Restore();
            Boots.Restore();
            Cloak.Restore();
            Weapon.Restore();
            Grimoire.Restore();
            Bag.Restore();
        }

        public void RefreshChangedSlots()
        {
            var equipment = Player.Read<Equipment>();
            Refresh("head", ref Head, equipment.ArmorHeadgearSlot);
            Refresh("chest", ref Chest, equipment.ArmorChestSlot);
            Refresh("gloves", ref Gloves, equipment.ArmorGlovesSlot);
            Refresh("legs", ref Legs, equipment.ArmorLegsSlot);
            Refresh("boots", ref Boots, equipment.ArmorFootgearSlot);
            Refresh("cloak", ref Cloak, equipment.CloakSlot);
            Refresh("weapon", ref Weapon, equipment.WeaponSlot);
            Refresh("grimoire", ref Grimoire, equipment.GrimoireSlot);
            Refresh("bag", ref Bag, equipment.BagSlot);
        }

        void Refresh(string slotName, ref TrackedItem tracked, EquipmentSlot slot)
        {
            var next = slot.SlotEntity.GetEntityOnServer();
            if (tracked.Item == next)
                return;

            var previous = tracked.Item;
            tracked.Restore();

            if (previous != Entity.Null)
                Record(previous, EventEntityChangeKind.Unequipped, "native.equipment.change", slotName);

            tracked = TrackedItem.FromSlot(slot);
            tracked.SetNoDurability();

            if (next != Entity.Null)
            {
                var key = EntityKey.From(next);
                var createdByEvent = !BaselineEntities.Contains(key) && !string.IsNullOrWhiteSpace(EventRunId);
                Record(next, EventEntityChangeKind.Equipped, "native.equipment.change", slotName, createdByEvent);
            }
        }

        public void Record(
            Entity entity,
            EventEntityChangeKind kind,
            string sourceAction,
            string stageId,
            bool createdByEvent = false)
        {
            if (entity == Entity.Null)
                return;

            var key = EntityKey.From(entity);
            if (!Changes.TryGetValue(key, out var record))
            {
                record = new EventEntityChangeRecord
                {
                    EntityIndex = key.Index,
                    EntityVersion = key.Version,
                    EventId = EventId,
                    EventRunId = EventRunId,
                    SourceAction = sourceAction?.Trim() ?? "",
                    StageId = stageId?.Trim() ?? "",
                    FirstObservedUtc = DateTime.UtcNow
                };
                Changes[key] = record;
            }

            record.LastObservedUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(sourceAction))
                record.SourceAction = sourceAction.Trim();
            if (!string.IsNullOrWhiteSpace(stageId))
                record.StageId = stageId.Trim();

            record.CreatedByEvent |= createdByEvent || kind == EventEntityChangeKind.Created;
            record.Modified |= kind is EventEntityChangeKind.Modified or EventEntityChangeKind.Equipped or EventEntityChangeKind.Unequipped;
            record.Destroyed |= kind == EventEntityChangeKind.Destroyed;
            record.LastChange = kind;
        }

        public EventTrackingSummary CreateSummary(ulong steamId) => new()
        {
            SteamId = steamId,
            EventId = EventId,
            EventRunId = EventRunId,
            BaselineEntityCount = BaselineEntityCount,
            TrackedEntityCount = Changes.Count,
            CreatedCount = Changes.Values.Count(change => change.CreatedByEvent),
            ModifiedCount = Changes.Values.Count(change => change.Modified),
            DestroyedCount = Changes.Values.Count(change => change.Destroyed)
        };
    }

    struct TrackedItem
    {
        public Entity Item;
        public float DurabilityValue;
        public DurabilityLossType LossType;
        public float TakeDamageFactor;
        public bool HasDurability;

        public static TrackedItem FromSlot(EquipmentSlot slot) => new()
        {
            Item = slot.SlotEntity.GetEntityOnServer()
        };

        public void SetNoDurability()
        {
            if (!Item.Exists() || !Item.Has<Durability>())
                return;

            var durability = Item.Read<Durability>();
            DurabilityValue = durability.Value;
            LossType = durability.LossType;
            TakeDamageFactor = durability.TakeDamageDurabilityLossFactor;
            HasDurability = true;

            durability.LossType = DurabilityLossType.None;
            durability.TakeDamageDurabilityLossFactor = 0f;
            Item.Write(durability);
        }

        public void Restore()
        {
            if (!HasDurability || !Item.Exists() || !Item.Has<Durability>())
                return;

            var durability = Item.Read<Durability>();
            durability.Value = DurabilityValue;
            durability.LossType = LossType;
            durability.TakeDamageDurabilityLossFactor = TakeDamageFactor;
            Item.Write(durability);
        }
    }

    readonly record struct EntityKey(int Index, int Version)
    {
        public static EntityKey From(Entity entity) => new(entity.Index, entity.Version);
    }
}

public enum EventEntityChangeKind
{
    Created,
    Modified,
    Equipped,
    Unequipped,
    Destroyed
}

public sealed class EventEntityChangeRecord
{
    public int EntityIndex { get; set; }
    public int EntityVersion { get; set; }
    public string EventId { get; set; } = "";
    public string EventRunId { get; set; } = "";
    public string StageId { get; set; } = "";
    public string SourceAction { get; set; } = "";
    public EventEntityChangeKind LastChange { get; set; }
    public bool CreatedByEvent { get; set; }
    public bool Modified { get; set; }
    public bool Destroyed { get; set; }
    public DateTime FirstObservedUtc { get; set; }
    public DateTime LastObservedUtc { get; set; }

    public EventEntityChangeRecord Clone() => new()
    {
        EntityIndex = EntityIndex,
        EntityVersion = EntityVersion,
        EventId = EventId,
        EventRunId = EventRunId,
        StageId = StageId,
        SourceAction = SourceAction,
        LastChange = LastChange,
        CreatedByEvent = CreatedByEvent,
        Modified = Modified,
        Destroyed = Destroyed,
        FirstObservedUtc = FirstObservedUtc,
        LastObservedUtc = LastObservedUtc
    };
}

public sealed class EventTrackingSummary
{
    public ulong SteamId { get; set; }
    public string EventId { get; set; } = "";
    public string EventRunId { get; set; } = "";
    public int BaselineEntityCount { get; set; }
    public int TrackedEntityCount { get; set; }
    public int CreatedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int DestroyedCount { get; set; }
}