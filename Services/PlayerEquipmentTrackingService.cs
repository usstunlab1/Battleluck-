using ProjectM.Shared;

namespace BattleLuck.Services;

public sealed class PlayerEquipmentTrackingService
{
    readonly Dictionary<ulong, TrackedPlayerEquipment> _tracked = new();

    public void StartTrackingPlayer(Entity player)
    {
        if (!player.Exists() || !player.Has<Equipment>())
            return;

        var steamId = player.GetSteamId();
        if (steamId == 0 || _tracked.ContainsKey(steamId))
            return;

        var tracked = TrackedPlayerEquipment.Capture(player);
        tracked.ApplyNoDurability();
        _tracked[steamId] = tracked;
        BattleLuckPlugin.LogInfo($"[EquipmentTrack] Tracking no-durability equipment for {steamId}.");
    }

    public void StopTrackingPlayer(ulong steamId)
    {
        if (!_tracked.Remove(steamId, out var tracked))
            return;

        tracked.Restore();
        BattleLuckPlugin.LogInfo($"[EquipmentTrack] Restored tracked equipment for {steamId}.");
    }

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
            _tracked[steamId] = tracked;
        }
    }

    public void RestoreAll()
    {
        foreach (var tracked in _tracked.Values)
            tracked.Restore();
        _tracked.Clear();
    }

    struct TrackedPlayerEquipment
    {
        public Entity Player;
        public TrackedItem Head;
        public TrackedItem Chest;
        public TrackedItem Gloves;
        public TrackedItem Legs;
        public TrackedItem Boots;
        public TrackedItem Cloak;
        public TrackedItem Weapon;
        public TrackedItem Grimoire;
        public TrackedItem Bag;

        public static TrackedPlayerEquipment Capture(Entity player)
        {
            var equipment = player.Read<Equipment>();
            return new TrackedPlayerEquipment
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
            Refresh(ref Head, equipment.ArmorHeadgearSlot);
            Refresh(ref Chest, equipment.ArmorChestSlot);
            Refresh(ref Gloves, equipment.ArmorGlovesSlot);
            Refresh(ref Legs, equipment.ArmorLegsSlot);
            Refresh(ref Boots, equipment.ArmorFootgearSlot);
            Refresh(ref Cloak, equipment.CloakSlot);
            Refresh(ref Weapon, equipment.WeaponSlot);
            Refresh(ref Grimoire, equipment.GrimoireSlot);
            Refresh(ref Bag, equipment.BagSlot);
        }

        static void Refresh(ref TrackedItem tracked, EquipmentSlot slot)
        {
            var next = slot.SlotEntity.GetEntityOnServer();
            if (tracked.Item == next)
                return;

            tracked.Restore();
            tracked = TrackedItem.FromSlot(slot);
            tracked.SetNoDurability();
        }
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
}
