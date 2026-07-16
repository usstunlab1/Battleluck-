using Unity.Entities;
using Unity.Collections;

namespace BattleLuck.ECS.Actions.Components;

/// <summary>
/// ECS action component for setting player blood type and quality.
/// Replaces the "set_blood" flow action string.
/// </summary>
public struct SetBloodAction
{
    public Entity TargetEntity;
    public FixedString64Bytes BloodType;
    public int Quality;
    public Entity SessionEntity;
}
