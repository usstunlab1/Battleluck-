using BattleLuck.Models;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services.Npc;

/// <summary>
/// Reads server-visible player state from ECS components and produces a
/// normalized PlayerObservation for the adaptive AI. Only depends on state
/// available to the server — no client input, animations, or player controls.
/// </summary>
public sealed class NpcTargetObserver
{
    public static NpcTargetObserver Instance { get; } = new();

    readonly List<PrefabGUID> _recentEffects = new();
    float _lastEffectReadTime;

    /// <summary>
    /// Build a PlayerObservation snapshot from the player entity, relative to the NPC entity.
    /// </summary>
    public PlayerObservation Observe(Entity playerEntity, Entity npcEntity)
    {
        if (!playerEntity.Exists())
            return new PlayerObservation();

        var steamId = playerEntity.GetSteamId();
        var npcPos = npcEntity.Exists() ? npcEntity.GetPosition() : float3.zero;
        var playerPos = playerEntity.GetPosition();
        var distance = npcEntity.Exists() ? math.distance(npcPos, playerPos) : 0f;

        var healthRatio = 1f;
        var isInCombat = false;
        var isCasting = false;
        var isDashing = false;
        var equippedWeapon = PrefabGUID.Empty;
        var weaponCategory = WeaponCategory.None;

        try
        {
            if (playerEntity.Has<Health>())
            {
                var health = playerEntity.Read<Health>();
                healthRatio = health.MaxHealth._Value > 0
                    ? health.Value / health.MaxHealth._Value
                    : 1f;
            }
        }
        catch { }

        try
        {
            if (playerEntity.Has<Equipment>())
            {
                var equipment = playerEntity.Read<Equipment>();
                equippedWeapon = equipment.Weapon;
                weaponCategory = ClassifyWeapon(equippedWeapon);
            }
        }
        catch { }

        try
        {
            if (playerEntity.Has<CastState>())
            {
                var castState = playerEntity.Read<CastState>();
                isCasting = castState.IsCasting;
            }
        }
        catch { }

        try
        {
            if (playerEntity.Has<Dashing>())
            {
                isDashing = playerEntity.Has<Dashing>();
            }
        }
        catch { }

        try
        {
            if (playerEntity.Has<AggroConsumer>())
            {
                var aggro = playerEntity.Read<AggroConsumer>();
                isInCombat = aggro.ActiveAggroTargets.Count > 0;
            }
        }
        catch { }

        // Determine movement direction relative to NPC
        var isMovingTowardNpc = false;
        var isMovingAwayFromNpc = false;

        try
        {
            if (playerEntity.Has<Velocity>())
            {
                var velocity = playerEntity.Read<Velocity>();
                var vel = velocity.Value;
                if (math.lengthsq(vel) > 0.01f)
                {
                    var dirToNpc = math.normalizesafe(npcPos - playerPos);
                    var dot = math.dot(math.normalizesafe(vel), dirToNpc);
                    isMovingTowardNpc = dot > 0.3f;
                    isMovingAwayFromNpc = dot < -0.3f;
                }
            }
        }
        catch { }

        // Read active buffs
        var activeBuffs = new HashSet<PrefabGUID>();
        try
        {
            if (playerEntity.Has<BuffBuffer>())
            {
                var buffBuffer = playerEntity.ReadBuffer<BuffBuffer>();
                for (int i = 0; i < buffBuffer.Length && i < 16; i++)
                {
                    var buff = buffBuffer[i];
                    if (buff.PrefabGUID != PrefabGUID.Empty)
                        activeBuffs.Add(buff.PrefabGUID);
                }
            }
        }
        catch { }

        // Read recent ability effects (sampled periodically)
        var recentEffects = new HashSet<PrefabGUID>();
        var now = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
        if (now - _lastEffectReadTime > 1f)
        {
            _lastEffectReadTime = now;
            _recentEffects.Clear();

            try
            {
                if (playerEntity.Has<AbilityEffectsBuffer>())
                {
                    var effects = playerEntity.ReadBuffer<AbilityEffectsBuffer>();
                    for (int i = 0; i < effects.Length && i < 8; i++)
                    {
                        var effect = effects[i];
                        if (effect.PrefabGUID != PrefabGUID.Empty)
                            _recentEffects.Add(effect.PrefabGUID);
                    }
                }
            }
            catch { }

            recentEffects = new HashSet<PrefabGUID>(_recentEffects);
        }

        return new PlayerObservation
        {
            SteamId = steamId,
            Position = playerPos,
            Velocity = playerPos - (npcEntity.Exists() ? playerEntity.GetPosition() : playerPos),
            DistanceToNpc = distance,
            HealthRatio = healthRatio,
            EquippedWeapon = equippedWeapon,
            WeaponCategory = weaponCategory,
            IsInCombat = isInCombat,
            IsCasting = isCasting,
            IsDashing = isDashing,
            IsMovingTowardNpc = isMovingTowardNpc,
            IsMovingAwayFromNpc = isMovingAwayFromNpc,
            ActiveBuffs = activeBuffs,
            RecentAbilityEffects = recentEffects
        };
    }

    static WeaponCategory ClassifyWeapon(PrefabGUID weapon)
    {
        var name = PrefabHelper.GetName(weapon) ?? "";
        if (name.Contains("bow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("crossbow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pistol", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rifle", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Ranged;
        if (name.Contains("staff", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("wand", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("scepter", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Magic;
        if (name.Contains("sword", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("axe", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mace", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("spear", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("scythe", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("hammer", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Melee;
        return WeaponCategory.None;
    }
}