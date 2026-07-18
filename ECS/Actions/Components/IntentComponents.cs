namespace BattleLuck.ECS.Actions.Components;

public struct EventParticipant
{
    public Entity SessionEntity;
    public FixedString64Bytes SessionId;
    public int TeamIndex;
    public int DeathCount;
    public bool Eliminated;
}

public struct ActionIntent
{
    public FixedString64Bytes ActionId;
    public FixedString64Bytes IntentId;
    public double CreatedAt;
    public FixedString64Bytes PrefabGuid;  // authoritative GUID string
    public float3 Position;
    public float Rotation;
    public Entity Caller;
    public Entity EventEntity;
}

public struct ValidatedTag { }
public struct ConsumedTag { }

public struct EventChest
{
    public Entity SessionEntity;
    public int RequiredKills;
    public bool Locked;
}
