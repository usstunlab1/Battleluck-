using System.Globalization;
using Unity.Mathematics;

namespace BattleLuck.Models;

/// <summary>
/// Converts between the community map grid and V Rising world coordinates.
/// Grid 20,20 is the world origin and each grid square spans 160 world units.
/// A grid coordinate represents a grid point (cell corner) by default.
/// Use <see cref="ToWorldCellCenter"/> for cell-center conversion.
/// </summary>
public readonly record struct WorldGridCoordinate(int Column, int Row)
{
    public const int OriginIndex = 20;
    public const float CellSize = 160f;

    // Map bounds (0..40 grid units for a typical V Rising map).
    public const int MinGridIndex = 0;
    public const int MaxGridIndex = 40;

    /// <summary>
    /// Convert this grid point to a world-space position.
    /// Grid (20,20) maps to world origin (0,0,0).
    /// </summary>
    public WorldCoordinatePosition ToWorldPoint(float height = 0f) => new(
        (Column - OriginIndex) * CellSize,
        height,
        (Row - OriginIndex) * CellSize);

    /// <summary>
    /// Convert this grid coordinate to the center of the corresponding cell.
    /// Applies a half-cell offset so (20,20) maps to (80, h, 80).
    /// </summary>
    public WorldCoordinatePosition ToWorldCellCenter(float height = 0f) => new(
        (Column - OriginIndex) * CellSize + CellSize / 2f,
        height,
        (Row - OriginIndex) * CellSize + CellSize / 2f);

    /// <summary>
    /// Backward-compatible alias for <see cref="ToWorldPoint"/>.
    /// </summary>
    [System.Obsolete("Use ToWorldPoint() or ToWorldCellCenter() for explicit semantics.")]
    public WorldCoordinatePosition ToWorld(float height = 0f) => ToWorldPoint(height);

    /// <summary>
    /// Returns true if this coordinate falls within the expected map bounds.
    /// </summary>
    public bool IsWithinMap(int min = MinGridIndex, int max = MaxGridIndex) =>
        Column >= min && Column <= max && Row >= min && Row <= max;

    public static WorldGridPosition FromWorld(float worldX, float worldZ) => new(
        worldX / CellSize + OriginIndex,
        worldZ / CellSize + OriginIndex);

    public static WorldGridPosition FromWorld(float3 world) => FromWorld(world.x, world.z);

    public static bool TryParse(string? value, out WorldGridCoordinate coordinate)
    {
        coordinate = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('G', 'g').Replace(':', ',');
        var parts = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var column) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
        {
            return false;
        }

        coordinate = new WorldGridCoordinate(column, row);
        return true;
    }

    public override string ToString() => $"{Column},{Row}";
}

public readonly record struct WorldCoordinatePosition(float X, float Y, float Z)
{
    public float3 ToFloat3() => new(X, Y, Z);

    /// <summary>
    /// Construct a WorldCoordinatePosition from a Unity float3.
    /// </summary>
    public static WorldCoordinatePosition FromFloat3(float3 v) => new(v.x, v.y, v.z);
}

public readonly record struct WorldGridPosition(float Column, float Row)
{
    /// <summary>
    /// Round to the nearest grid point using MidpointRounding.AwayFromZero.
    /// </summary>
    public WorldGridCoordinate NearestPoint => new(
        (int)MathF.Round(Column, MidpointRounding.AwayFromZero),
        (int)MathF.Round(Row, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Floor to the containing cell (always rounds toward negative infinity).
    /// </summary>
    public WorldGridCoordinate ContainingCell => new(
        (int)MathF.Floor(Column),
        (int)MathF.Floor(Row));

    /// <summary>
    /// Backward-compatible alias for <see cref="NearestPoint"/>.
    /// </summary>
    [System.Obsolete("Use NearestPoint for clarity.")]
    public WorldGridCoordinate NearestCell => NearestPoint;

    public override string ToString() => FormattableString.Invariant($"{Column:F2},{Row:F2}");
}
