using Unity.Entities;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for enabling PvP for a player.
/// Replaces the "enable_pvp" flow action string.
/// </summary>
public struct EnablePvPAction
{
    public Entity TargetEntity;
    public int ZoneHash;
    public Entity SessionEntity;
}
