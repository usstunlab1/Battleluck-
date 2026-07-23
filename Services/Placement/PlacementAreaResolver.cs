using Unity.Mathematics;

namespace BattleLuck.Services.Placement;

/// <summary>
/// Canonical radius contract for area-based placement actions.
///
/// Resolution priority for center:
/// 1. explicit action center
/// 2. explicit target position
/// 3. active zone center
/// 4. sender position only when the request explicitly says "here"
///
/// Resolution priority for radius:
/// 1. explicit positive action radius
/// 2. active zone radius
/// 3. validation failure (never silently use 0 or unlimited)
/// </summary>
public static class PlacementAreaResolver
{
    public enum RadiusSourceType { ExplicitRadius, ZoneRadius, ValidationFailure }
    public enum ShapeType { Circle, Polygon }

    public sealed class PlacementArea
    {
        public float3 Center { get; init; }
        public float EffectiveRadius { get; init; }
        public RadiusSourceType RadiusSource { get; init; }
        public ShapeType Shape { get; init; }
        public int ZoneHash { get; init; }
        public float3[]? Polygon { get; init; }
        public float BoundaryPadding { get; init; }
        public bool Valid { get; init; }
        public string Error { get; init; } = "";
    }

    /// <summary>
    /// Resolve the placement area from an action request and active zone context.
    /// </summary>
    /// <param name="requestedRadius">The radius from the action parameters (0, negative, or null means "use zone").</param>
    /// <param name="requestedCenter">Explicit center from the action parameters (null means "resolve from zone or sender").</param>
    /// <param name="activeZone">The active event zone the player is in.</param>
    /// <param name="senderPosition">The player's current position (used only for "here" requests).</param>
    /// <param name="requestExplicitlySaysHere">Whether the user explicitly said "here".</param>
    /// <param name="boundaryPadding">Padding to subtract from the effective radius.</param>
    public static PlacementArea Resolve(
        float? requestedRadius,
        float3? requestedCenter,
        ZoneDefinition? activeZone,
        float3 senderPosition,
        bool requestExplicitlySaysHere,
        float boundaryPadding = 1.0f)
    {
        // ── Center resolution ──────────────────────────────────────────────
        float3 center;
        if (requestedCenter.HasValue && math.lengthsq(requestedCenter.Value) > 0.0001f)
        {
            center = requestedCenter.Value;
        }
        else if (activeZone != null)
        {
            center = ZoneCenter(activeZone);
        }
        else if (requestExplicitlySaysHere)
        {
            center = senderPosition;
        }
        else
        {
            return new PlacementArea
            {
                Valid = false,
                Error = "No placement center could be determined. Specify a position, zone, or 'here'."
            };
        }

        // ── Radius resolution ──────────────────────────────────────────────
        float effectiveRadius;
        RadiusSourceType source;

        if (requestedRadius.HasValue && requestedRadius.Value > 0f)
        {
            effectiveRadius = requestedRadius.Value;
            source = RadiusSourceType.ExplicitRadius;
        }
        else if (activeZone != null && activeZone.Radius > 0f)
        {
            effectiveRadius = activeZone.Radius;
            source = RadiusSourceType.ZoneRadius;
        }
        else
        {
            return new PlacementArea
            {
                Center = center,
                Valid = false,
                Error = "No radius could be determined. Specify an explicit radius or ensure the event zone has a defined radius."
            };
        }

        // ── Shape resolution ───────────────────────────────────────────────
        // Default to circular shape. Polygon support is ready for when the
        // zone model adds polygon boundaries.
        ShapeType shape = ShapeType.Circle;
        float3[]? polygon = null;

        // ── Logging ────────────────────────────────────────────────────────
        BattleLuckPlugin.LogInfo(
            $"[PlacementArea] resolved-center=({center.x:F1},{center.y:F1},{center.z:F1}) " +
            $"resolved-radius={effectiveRadius:F1} source={source} shape={shape} " +
            $"zoneHash={activeZone?.Hash ?? 0} boundaryPadding={boundaryPadding:F1}");

        return new PlacementArea
        {
            Center = center,
            EffectiveRadius = effectiveRadius,
            RadiusSource = source,
            Shape = shape,
            ZoneHash = activeZone?.Hash ?? 0,
            Polygon = polygon,
            BoundaryPadding = boundaryPadding,
            Valid = true
        };
    }

    /// <summary>
    /// Generate deterministic placement positions for a circular area.
    /// Uses a grid layout with the configured tile spacing.
    /// </summary>
    public static List<float3> GenerateCircularPlacements(
        PlacementArea area,
        float tileSpacing,
        float tileHalfDiagonal)
    {
        var positions = new List<float3>();
        if (!area.Valid || area.Shape == ShapeType.Polygon)
            return positions;

        var radius = area.EffectiveRadius - area.BoundaryPadding - tileHalfDiagonal;
        if (radius <= 0f)
            return positions;

        // Generate a grid within the bounding box
        var halfSide = (int)MathF.Ceiling(radius / tileSpacing);

        for (var ix = -halfSide; ix <= halfSide; ix++)
        {
            for (var iz = -halfSide; iz <= halfSide; iz++)
            {
                var cellCenter = area.Center + new float3(ix * tileSpacing, 0f, iz * tileSpacing);
                var dist = math.distance(cellCenter.xz, area.Center.xz);

                // Only include cells whose full tile footprint fits inside the radius
                if (dist <= radius)
                    positions.Add(cellCenter);
            }
        }

        return positions;
    }

    /// <summary>
    /// Generate deterministic placement positions for a polygon area.
    /// Uses point-in-polygon checks.
    /// </summary>
    public static List<float3> GeneratePolygonPlacements(
        PlacementArea area,
        float tileSpacing,
        float tileHalfDiagonal)
    {
        var positions = new List<float3>();
        if (!area.Valid || area.Shape != ShapeType.Polygon || area.Polygon == null || area.Polygon.Length < 3)
            return positions;

        var effectiveRadius = area.EffectiveRadius - area.BoundaryPadding - tileHalfDiagonal;
        if (effectiveRadius <= 0f)
            return positions;

        var halfSide = (int)MathF.Ceiling(effectiveRadius / tileSpacing);

        for (var ix = -halfSide; ix <= halfSide; ix++)
        {
            for (var iz = -halfSide; iz <= halfSide; iz++)
            {
                var cellCenter = area.Center + new float3(ix * tileSpacing, 0f, iz * tileSpacing);

                // Point-in-polygon check
                if (IsPointInPolygon(cellCenter.xz, area.Polygon))
                    positions.Add(cellCenter);
            }
        }

        return positions;
    }

    /// <summary>
    /// Calculate the estimated tile count for preview purposes.
    /// </summary>
    public static int EstimateTileCount(PlacementArea area, float tileSpacing, float tileHalfDiagonal)
    {
        if (!area.Valid)
            return 0;

        if (area.Shape == ShapeType.Circle)
        {
            var positions = GenerateCircularPlacements(area, tileSpacing, tileHalfDiagonal);
            return positions.Count;
        }
        else
        {
            var positions = GeneratePolygonPlacements(area, tileSpacing, tileHalfDiagonal);
            return positions.Count;
        }
    }

    static float3 ZoneCenter(ZoneDefinition zone)
    {
        var center = zone.Position.ToFloat3();
        if (math.lengthsq(center) > 0.0001f)
            return center;
        return zone.TeleportSpawn.ToFloat3();
    }

    /// <summary>
    /// Check if a 2D point is inside a polygon using ray casting.
    /// </summary>
    static bool IsPointInPolygon(float2 point, float3[] polygon)
    {
        var inside = false;
        var j = polygon.Length - 1;

        for (var i = 0; i < polygon.Length; i++)
        {
            var pi = polygon[i].xz;
            var pj = polygon[j].xz;

            if ((pi.y > point.y) != (pj.y > point.y) &&
                point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }
}

/// <summary>
/// Extension for float3 to expose .xz property for 2D operations.
/// </summary>
public static class Float3Extensions
{
    public static float2 xz(this float3 v) => new(v.x, v.z);
}

/// <summary>
/// Configuration for placement operations.
/// </summary>
public sealed class PlacementConfig
{
    public float DefaultTileSpacing { get; set; } = 2.5f;
    public float BoundaryPadding { get; set; } = 1.0f;
    public int MaxPlacementsPerAction { get; set; } = 1000;
    public int MaxPlacementsPerTick { get; set; } = 20;
    public int MaxSpawnsPerTick { get; set; } = 10;
    public int MaxDestroysPerTick { get; set; } = 25;
    public int MaxRollbackWritesPerTick { get; set; } = 50;
    public int RequireApprovalAbove { get; set; } = 200;
}