using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Locks players to a specific Y height (floor) while inside arena zones.
/// Call LockPlayerToFloor when a player enters; call Tick every server frame
/// to enforce the Y constraint. Unlock on zone exit.
/// </summary>
public static class FloorLockService
{
    // zoneHash → floor Y height
    static readonly Dictionary<int, float> _zoneFloors = new();

    // steamId → (zoneHash, floorY)
    static readonly Dictionary<ulong, (int ZoneHash, float FloorY)> _lockedPlayers = new();

    /// <summary>Set the floor Y for an entire zone. First player enter triggers this globally.</summary>
    public static void SetZoneFloor(int zoneHash, float floorY)
    {
        _zoneFloors[zoneHash] = floorY;
        BattleLuckPlugin.LogInfo($"[FloorLock] Zone {zoneHash} floor set to Y={floorY}");
    }

    /// <summary>Lock a player to a zone's floor height.</summary>
    public static void LockPlayerToFloor(Entity playerCharacter, float floorY)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        // Find which zone this player is in based on position
        float3 pos = playerCharacter.GetPosition();
        int zoneHash = 0;
        foreach (var kv in _zoneFloors)
        {
            zoneHash = kv.Key;
            break; // Use first registered zone if we can't detect
        }

        _lockedPlayers[steamId] = (zoneHash, floorY);

        // Set floor for the zone globally (first player sets it)
        if (!_zoneFloors.ContainsKey(zoneHash) || zoneHash == 0)
        {
            // No specific zone — just lock the player
        }

        // Snap player to floor immediately
        playerCharacter.SetPosition(new float3(pos.x, floorY, pos.z));
        BattleLuckPlugin.LogInfo($"[FloorLock] Locked player {steamId} to floor Y={floorY}");
    }

    /// <summary>Lock a player to a specific zone's floor.</summary>
    public static void LockPlayerToFloor(Entity playerCharacter, int zoneHash, float floorY)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        _zoneFloors[zoneHash] = floorY;
        _lockedPlayers[steamId] = (zoneHash, floorY);

        float3 pos = playerCharacter.GetPosition();
        playerCharacter.SetPosition(new float3(pos.x, floorY, pos.z));
        BattleLuckPlugin.LogInfo($"[FloorLock] Locked player {steamId} to zone {zoneHash} floor Y={floorY}");
    }

    /// <summary>Unlock a player (zone exit).</summary>
    public static void UnlockPlayer(ulong steamId)
    {
        if (_lockedPlayers.Remove(steamId))
            BattleLuckPlugin.LogInfo($"[FloorLock] Unlocked player {steamId}");
    }

    /// <summary>Clear all locks and zone floors (mode end / reset).</summary>
    public static void Reset()
    {
        _lockedPlayers.Clear();
        _zoneFloors.Clear();
    }

    /// <summary>
    /// Call every server tick. Enforces Y position for all locked players.
    /// Players who jump/fall get snapped back to their assigned floor.
    /// </summary>
    public static void Tick(List<Entity> onlinePlayers)
    {
        if (_lockedPlayers.Count == 0) return;

        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer()) continue;

            ulong steamId = player.GetSteamId();
            if (!_lockedPlayers.TryGetValue(steamId, out var lockInfo)) continue;

            float3 pos = player.GetPosition();
            float diff = math.abs(pos.y - lockInfo.FloorY);

            // Only correct if drifted more than 0.5 units from the locked floor
            if (diff > 0.5f)
            {
                player.SetPosition(new float3(pos.x, lockInfo.FloorY, pos.z));
            }
        }
    }
}
