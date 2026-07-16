namespace BattleLuck.Models
{
    /// <summary>
    /// Servant command types for BattleLuck-custom servant control.
    /// AI-understandable: Attack, Defend, Follow, Hold, Retreat are the primary commands.
    /// </summary>
    public enum ServantCommand
    {
        Attack = 0,
        Defend = 1,
        Follow = 2,
        Hold = 3,
        Retreat = 4
    }
}
