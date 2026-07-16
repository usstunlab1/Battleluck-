using System;

namespace BattleLuck.Models
{
    /// <summary>
    /// Servant type flags aligned with VRising data models.
    /// AI-understandable: Blacksmith, Lumberjack, Tailor, Officer, Guard are the primary servant types.
    /// Uses Flags attribute to support multiple types per servant.
    /// </summary>
    [Flags]
    public enum ServantType
    {
        None = 0,
        Blacksmith = 1,
        Lumberjack = 2,
        Tailor = 4,
        Officer = 8,
        Guard = 16
    }
}
