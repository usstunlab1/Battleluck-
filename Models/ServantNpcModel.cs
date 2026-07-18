namespace BattleLuck.Models
{
    /// <summary>
    /// AI-understandable servant NPC model aligned with VRising data models.
    /// Contains all servant data that AI/Qwen can use to make decisions.
    /// </summary>
    public class ServantNpcModel
    {
        /// <summary>Unique identifier for this servant NPC</summary>
        public int ServantNpcId { get; set; }
        
        /// <summary>Prefab name for spawning (e.g., "CHAR_Skeleton_Warrior_Servant")</summary>
        public string PrefabName { get; set; } = string.Empty;
        
        /// <summary>Display icon path</summary>
        public string Icon { get; set; } = string.Empty;
        
        /// <summary>Servant type (Blacksmith, Lumberjack, Tailor, Officer, Guard)</summary>
        public ServantType ServantType { get; set; }
        
        /// <summary>Servant faction (Cursed, Dunley, Farbane, Silver)</summary>
        public ServantFaction ServantFaction { get; set; }
        
        /// <summary>Base NPC ID (non-servant version)</summary>
        public int BaseNpcId { get; set; }
        
        /// <summary>Blood type ID for this servant</summary>
        public int BloodTypeId { get; set; }
        
        /// <summary>Blood type name (AI-friendly)</summary>
        public string BloodTypeName { get; set; } = string.Empty;
        
        /// <summary>Available perks for this servant</summary>
        public List<ServantPerkModel> ServantPerks { get; set; } = new();
        
        /// <summary>Missions this servant can complete</summary>
        public List<ServantMissionModel> MatchingMissions { get; set; } = new();
        
        /// <summary>Localized name for UI display</summary>
        public string LocalizedName { get; set; } = string.Empty;
        
        /// <summary>Localized description</summary>
        public string LocalizedDescription { get; set; } = string.Empty;
        
        /// <summary>Health value</summary>
        public float Health { get; set; }
        
        /// <summary>Level of the servant</summary>
        public int Level { get; set; }
        
        /// <summary>Whether this is a VBlood servant</summary>
        public bool IsVBlood { get; set; }
    }
}
