namespace BattleLuck.Models
{
    /// <summary>
    /// AI-understandable servant perk model aligned with VRising data models.
    /// Contains perk data that AI/Qwen can use to understand servant capabilities.
    /// </summary>
    public class ServantPerkModel
    {
        /// <summary>Unique identifier for this perk</summary>
        public int ServantPerkId { get; set; }
        
        /// <summary>Prefab name for the perk</summary>
        public string PrefabName { get; set; } = string.Empty;
        
        /// <summary>Display icon path</summary>
        public string Icon { get; set; } = string.Empty;
        
        /// <summary>Blood type ID this perk is associated with (0 if faction-based)</summary>
        public int BloodTypeId { get; set; }
        
        /// <summary>Blood type name (AI-friendly)</summary>
        public string BloodTypeName { get; set; } = string.Empty;
        
        /// <summary>Servant faction this perk is associated with (Unknown if blood-based)</summary>
        public ServantFaction ServantFaction { get; set; }
        
        /// <summary>Servants that have this perk</summary>
        public List<ServantNpcModel> ServantNpcs { get; set; } = new();
        
        /// <summary>Missions that require this perk</summary>
        public List<ServantMissionModel> MissionsWithPerk { get; set; } = new();
        
        /// <summary>Localized name for UI display</summary>
        public string LocalizedName { get; set; } = string.Empty;
        
        /// <summary>Localized description</summary>
        public string LocalizedDescription { get; set; } = string.Empty;
    }
}
