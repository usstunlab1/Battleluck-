using ServantCommand = BattleLuck.Models.ServantCommand;
using ServantFaction = BattleLuck.Models.ServantFaction;
using ServantFormation = BattleLuck.Models.ServantFormation;
using ServantType = BattleLuck.Models.ServantType;

namespace BattleLuck.ECS.Actions.Components;

// Boss Teaming / Servant Mechanics Actions

public struct BossAddServantAction
{
    public Entity BossEntity;
    public Entity ServantEntity;
    public ServantType ServantType; // Blacksmith, Lumberjack, Tailor, Officer, Guard (from VRising data models)
    public ServantFaction ServantFaction; // Cursed, Dunley, Farbane, Silver (from VRising data models)
    public Entity SessionEntity;
}

public struct BossRemoveServantAction
{
    public Entity BossEntity;
    public Entity ServantEntity;
    public Entity SessionEntity;
}

public struct BossCommandServantsAction
{
    public Entity BossEntity;
    public ServantCommand Command; // Attack, Defend, Follow, Hold, Retreat
    public Entity TargetEntity;    // optional target
    public float Radius;           // optional area command
    public Entity SessionEntity;
}

public struct BossSpawnServantGroupAction
{
    public Entity BossEntity;
    public FixedString64Bytes Prefab; // Prefab name as string (e.g., "CHAR_Skeleton_Warrior")
    public float3 Position;
    public int Count;
    public int DelaySeconds;        // spawn delay
    public int LifetimeSeconds;    // despawn after time (-1 = permanent)
    public int IntervalSeconds;    // spawn interval for groups
    public ServantFormation Formation; // Circle, Line, Swarm, Guard
    public ServantType ServantType; // Blacksmith, Lumberjack, Tailor, Officer, Guard
    public ServantFaction ServantFaction; // Cursed, Dunley, Farbane, Silver
    public Entity SessionEntity;
}