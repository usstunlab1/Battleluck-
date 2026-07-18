namespace BattleLuck.Models;

/// <summary>
/// Converts between the community map grid and V Rising world coordinates.
/// Grid 20,20 is the world origin and each grid square spans 160 world units.
/// </summary>
public readonly record struct WorldGridCoordinate(int Column, int Row)
{
    public const int OriginIndex = 20;
    public const float CellSize = 160f;

    public WorldCoordinatePosition ToWorld(float height = 0f) => new(
        (Column - OriginIndex) * CellSize,
        height,
        (Row - OriginIndex) * CellSize);

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
}

public readonly record struct WorldGridPosition(float Column, float Row)
{
    public WorldGridCoordinate NearestCell => new(
        (int)MathF.Round(Column),
        (int)MathF.Round(Row));

    public override string ToString() => FormattableString.Invariant($"{Column:F2},{Row:F2}");
}
