using System.Collections.Generic;

namespace BattleLuck.Models
{
    /// <summary>
    /// AI-understandable servant mission model aligned with VRising data models.
    /// Contains mission data that AI/Qwen can use to assign servants to tasks.
    /// </summary>
    public class ServantMissionModel
    {
        /// <summary>Unique identifier for this mission</summary>
        public int ServantMissionId { get; set; }
        
        /// <summary>Prefab name for the mission</summary>
        public string PrefabName { get; set; } = string.Empty;
        
        /// <summary>Display icon path</summary>
        public string Icon { get; set; } = string.Empty;
        
        /// <summary>Location where this mission takes place (AI-friendly name)</summary>
        public string Location { get; set; } = string.Empty;
        
        /// <summary>Required servant perks for this mission</summary>
        public List<int> RequiredServantPerkIds { get; set; } = new();
        
        /// <summary>Required servant perk models (AI-friendly)</summary>
        public List<ServantPerkModel> RequiredServantPerks { get; set; } = new();
        
        /// <summary>Number of servant slots available for this mission</summary>
        public int ServantSlots { get; set; }
        
        /// <summary>Mission difficulty (1-10 scale, AI-friendly)</summary>
        public int Difficulty { get; set; }
        
        /// <summary>Drop tables for mission rewards</summary>
        public List<string> DropTableNames { get; set; } = new();
        
        /// <summary>Servants that can complete this mission</summary>
        public List<ServantNpcModel> MatchingServants { get; set; } = new();
        
        /// <summary>Localized name for UI display</summary>
        public string LocalizedName { get; set; } = string.Empty;
        
        /// <summary>Localized description</summary>
        public string LocalizedDescription { get; set; } = string.Empty;
    }
}
