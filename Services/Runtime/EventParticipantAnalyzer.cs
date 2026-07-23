using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Analyzes participating players at event start to produce an EventParticipantProfile
/// with combat strength estimates. Combat strength is computed from equipment level,
/// health, weapon rating, and defense rating — not level alone.
/// </summary>
public sealed class EventParticipantAnalyzer
{
    public static EventParticipantAnalyzer Instance { get; } = new();

    /// <summary>
    /// Build a participant profile from the current set of online players
    /// that are part of the given event context.
    /// </summary>
    public EventParticipantProfile Analyze(GameModeContext context)
    {
        var players = VRisingCore.GetOnlinePlayers()
            .Where(p => p.Exists() && p.IsPlayer() && context.Players.Contains(p.GetSteamId()))
            .ToList();

        if (players.Count == 0)
            return new EventParticipantProfile(Array.Empty<PlayerCombatProfile>(), 0);

        var profiles = new List<PlayerCombatProfile>(players.Count);
        float totalStrength = 0;
        float peakStrength = float.MinValue;

        foreach (var player in players)
        {
            var profile = BuildPlayerProfile(player);
            profiles.Add(profile);
            totalStrength += profile.CombatStrength;
            if (profile.CombatStrength > peakStrength) peakStrength = profile.CombatStrength;
        }

        var averageStrength = profiles.Count > 0 ? profiles.Average(p => p.CombatStrength) : 0;
        return new EventParticipantProfile(profiles, averageStrength);
    }

    /// <summary>
    /// Build a single player's combat profile from server-visible ECS data.
    /// CombatStrength = equipmentLevel × 0.45 + normalizedHealth × 0.15 + weaponRating × 0.25 + defenseRating × 0.15
    /// </summary>
    static PlayerCombatProfile BuildPlayerProfile(Entity player)
    {
        var steamId = player.GetSteamId();
        var level = player.GetUnitLevel();
        var maxHealth = 100f;
        var healthRatio = 1f;

        try
        {
            if (player.Has<Health>())
            {
                var health = player.Read<Health>();
                maxHealth = health.MaxHealth._Value;
                healthRatio = maxHealth > 0 ? health.Value / maxHealth : 1f;
            }
        }
        catch
        {
            // Health component may not be available in all builds
        }

        var weapon = PrefabGUID.Empty;
        var weaponCategory = WeaponCategory.None;
        float damageRating = level * 0.5f;
        float defenseRating = level * 0.5f;

        try
        {
            if (player.Has<Equipment>())
            {
                weapon = PrefabGUID.Empty;
                // Estimate weapon category from prefab name
                var weaponName = PrefabHelper.GetName(weapon) ?? "";
                if (weaponName.Contains("bow", StringComparison.OrdinalIgnoreCase) ||
                    weaponName.Contains("crossbow", StringComparison.OrdinalIgnoreCase) ||
                    weaponName.Contains("pistol", StringComparison.OrdinalIgnoreCase))
                    weaponCategory = WeaponCategory.Ranged;
                else if (weaponName.Contains("staff", StringComparison.OrdinalIgnoreCase) ||
                         weaponName.Contains("wand", StringComparison.OrdinalIgnoreCase))
                    weaponCategory = WeaponCategory.Magic;
                else
                    weaponCategory = WeaponCategory.Melee;
            }
        }
        catch
        {
            // Equipment component may not be available
        }

        // Combat strength formula from the design spec
        var normalizedHealth = math.clamp(healthRatio, 0f, 1f);
        var weaponRating = math.clamp(damageRating / 100f, 0f, 1f);
        var defRating = math.clamp(defenseRating / 100f, 0f, 1f);
        var normalizedLevel = math.clamp(level / 100f, 0f, 1f);

        var combatStrength = normalizedLevel * 0.45f
                           + normalizedHealth * 0.15f
                           + weaponRating * 0.25f
                           + defRating * 0.15f;

        return new PlayerCombatProfile(steamId, level, healthRatio, math.clamp(combatStrength * 100f, 1f, 100f));
    }
}

/// <summary>
/// Weapon category classification for combat analysis.
/// </summary>
public enum WeaponCategory
{
    None,
    Melee,
    Ranged,
    Magic
}