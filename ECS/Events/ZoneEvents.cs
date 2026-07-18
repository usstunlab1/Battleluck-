namespace BattleLuck.ECS.Events;

/// <summary>
/// ECS event component for zone enter events.
/// Replaces ZoneEnterEvent managed class.
/// </summary>
public struct ZoneEnterEventComponent
{
    public Entity PlayerEntity;
    public ulong SteamId;
    public FixedString64Bytes ZoneId;
    public Entity SessionEntity;
}

/// <summary>
/// ECS event component for zone exit events.
/// Replaces ZoneExitEvent managed class.
/// </summary>
public struct ZoneExitEventComponent
{
    public Entity PlayerEntity;
    public ulong SteamId;
    public FixedString64Bytes ZoneId;
    public Entity SessionEntity;
}
