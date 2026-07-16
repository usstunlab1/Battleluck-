using ProjectM;
using ProjectM.Network;
using Unity.Entities;

/// <summary>
/// Toggles building placement restrictions during events via V Rising's
/// SetDebugSettingEvent system — the same mechanism KindredSchematics uses.
/// Disables placement restrictions, castle limits, floor restrictions,
/// and enables free building placement for all players.
/// </summary>
public static class BuildingRestrictionController
{
    static bool _disabled;

    public static SetDebugSettingEvent BuildingPlacementRestrictionsDisabledSetting = new()
    {
        SettingType = DebugSettingType.BuildingPlacementRestrictionsDisabled,
        Value = false
    };

    public static SetDebugSettingEvent CastleLimitsDisabledSetting = new()
    {
        SettingType = DebugSettingType.CastleLimitsDisabled,
        Value = false
    };

    public static SetDebugSettingEvent FloorPlacementRestrictionsSetting = new()
    {
        SettingType = DebugSettingType.FloorPlacementRestrictionsDisabled,
        Value = false
    };

    public static SetDebugSettingEvent FreeBuildingPlacementSetting = new()
    {
        SettingType = DebugSettingType.FreeBuildingPlacementEnabled,
        Value = true
    };

    /// <summary>True when building restrictions are currently bypassed.</summary>
    public static bool RestrictionsDisabled => _disabled;

    /// <summary>Disable all building placement restrictions for the duration of an event.</summary>
    public static void DisableRestrictions()
    {
        if (_disabled) return;
        _disabled = true;
        ApplyDebugSettings(true);
        BattleLuckPlugin.LogInfo("[Building] Restrictions disabled via debug settings for event.");
    }

    /// <summary>Restore normal building placement restrictions.</summary>
    public static void EnableRestrictions()
    {
        if (!_disabled) return;
        _disabled = false;
        ApplyDebugSettings(false);
        BattleLuckPlugin.LogInfo("[Building] Restrictions restored via debug settings.");
    }

    /// <summary>Force-reset state (e.g. on plugin unload).</summary>
    public static void Reset()
    {
        if (_disabled)
            ApplyDebugSettings(false);
        _disabled = false;
    }

    static void ApplyDebugSettings(bool disable)
    {
        try
        {
            var des = VRisingCore.DebugEventsSystem;

            BuildingPlacementRestrictionsDisabledSetting.Value = disable;
            des.SetDebugSetting(0, ref BuildingPlacementRestrictionsDisabledSetting);

            CastleLimitsDisabledSetting.Value = disable;
            des.SetDebugSetting(0, ref CastleLimitsDisabledSetting);

            FloorPlacementRestrictionsSetting.Value = disable;
            des.SetDebugSetting(0, ref FloorPlacementRestrictionsSetting);

            FreeBuildingPlacementSetting.Value = disable;
            des.SetDebugSetting(0, ref FreeBuildingPlacementSetting);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[Building] Failed to apply debug settings: {ex.Message}");
        }
    }
}
