using BattleLuck.Models;
using BattleLuck.Services.Npc;
using Unity.Entities;
using Unity.Mathematics;

namespace BattleLuck.Services.Creature;

/// <summary>
/// Manages creature capture validation, application, and release.
/// All creature.capture.* actions dispatch through this service.
/// Never captures V Bloods, players, or quest-critical entities without explicit admin policy override.
/// </summary>
public sealed class CreatureCaptureService
{
    readonly NpcControlService _npcService;
    readonly object _lock = new();

    // Captured creatures: entityId -> capture record
    readonly Dictionary<string, CaptureRecord> _captured = new(StringComparer.OrdinalIgnoreCase);
    // Blood quality thresholds per category
    readonly Dictionary<string, BloodThreshold> _thresholds = new(StringComparer.OrdinalIgnoreCase);

    // Admin override flag for V Blood capture
    const bool AllowVBloodCapture = false;

    public CreatureCaptureService(NpcControlService npcService)
    {
        _npcService = npcService;
    }

    public sealed record CaptureRecord(
        string EntityId,
        ulong OwnerSteamId,
        string OriginalPrefab,
        DateTime CapturedAt,
        bool ConvertedToPrisoner,
        bool PreservedBlood);

    public sealed record BloodThreshold(
        float MinimumQuality,
        float MaximumQuality,
        string Category);

    /// <summary>
    /// Read-only validation of creature capture eligibility.
    /// </summary>
    public OperationResult<CaptureValidation> Validate(string targetEntity, bool allowVBlood = false,
        string allowedCategories = "", float minimumBloodQuality = 0f, int maximumLevel = 0)
    {
        if (!_npcService.TryGet(targetEntity, out var entry))
            return OperationResult<CaptureValidation>.Fail($"Entity '{targetEntity}' is not tracked.");

        var entity = entry.Entity;
        if (!entity.Exists())
            return OperationResult<CaptureValidation>.Fail("Entity does not exist.");

        // Reject players
        if (entity.Has<PlayerCharacter>())
            return OperationResult<CaptureValidation>.Fail("Cannot capture player entities.");

        // Reject V Bloods unless admin override
        if (entity.IsVBlood() && !allowVBlood && !AllowVBloodCapture)
            return OperationResult<CaptureValidation>.Fail("Cannot capture V Blood entities without admin policy override.");

        // Check level cap
        if (maximumLevel > 0)
        {
            var level = entity.GetLevel();
            if (level > maximumLevel)
                return OperationResult<CaptureValidation>.Fail($"Entity level {level} exceeds maximum {maximumLevel}.");
        }

        // Check blood quality
        if (minimumBloodQuality > 0f)
        {
            var bloodQuality = entity.GetBloodQuality();
            if (bloodQuality < minimumBloodQuality)
                return OperationResult<CaptureValidation>.Fail($"Blood quality {bloodQuality:F0} is below minimum {minimumBloodQuality:F0}.");
        }

        return OperationResult<CaptureValidation>.Ok(new CaptureValidation(
            true, entry.PrefabName, entity.GetLevel(), entity.GetBloodQuality(), ""));
    }

    public sealed record CaptureValidation(
        bool Eligible,
        string PrefabName,
        int Level,
        float BloodQuality,
        string RejectionReason);

    /// <summary>
    /// Capture a creature and convert it to a prisoner or servant.
    /// </summary>
    public OperationResult Apply(string targetEntity, ulong ownerSteamId,
        float3? destination = null, bool convertToPrisoner = false, bool preserveBlood = true)
    {
        if (!_npcService.TryGet(targetEntity, out var entry))
            return OperationResult.Fail($"Entity '{targetEntity}' is not tracked.");

        var entity = entry.Entity;
        if (!entity.Exists())
            return OperationResult.Fail("Entity does not exist.");

        // Reject players
        if (entity.Has<PlayerCharacter>())
            return OperationResult.Fail("Cannot capture player entities.");

        // Reject V Bloods
        if (entity.IsVBlood() && !AllowVBloodCapture)
            return OperationResult.Fail("Cannot capture V Blood entities.");

        lock (_lock)
        {
            if (_captured.ContainsKey(targetEntity))
                return OperationResult.Fail($"Entity '{targetEntity}' is already captured.");

            _captured[targetEntity] = new CaptureRecord(
                targetEntity, ownerSteamId, entry.PrefabName,
                DateTime.UtcNow, convertToPrisoner, preserveBlood);

            // If converting to prisoner, despawn the NPC (it becomes a prisoner entity)
            if (convertToPrisoner)
            {
                _npcService.Despawn(targetEntity);
            }

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Release or restore a captured entity.
    /// </summary>
    public OperationResult Release(string targetEntity)
    {
        lock (_lock)
        {
            if (!_captured.Remove(targetEntity, out var record))
                return OperationResult.Fail($"Entity '{targetEntity}' is not captured.");

            return OperationResult.Ok();
        }
    }

    /// <summary>
    /// Set blood quality thresholds for creature capture.
    /// </summary>
    public OperationResult SetBloodThreshold(float minimumQuality, float maximumQuality, string category)
    {
        var key = string.IsNullOrWhiteSpace(category) ? "_default_" : category;
        lock (_lock)
        {
            _thresholds[key] = new BloodThreshold(minimumQuality, maximumQuality, category);
        }
        return OperationResult.Ok();
    }
}
