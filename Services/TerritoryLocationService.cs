using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Tiles;

namespace BattleLuck.Services;

/// <summary>
/// Resolves castle territories to world positions for teleport commands.
/// Read-only counterpart to <see cref="CastleTileOwnershipService"/>: it never
/// mutates territory state, so it is safe to call from a VCF command context.
/// Territory ids are the live <see cref="CastleTerritory"/> entity indices.
/// </summary>
public static class TerritoryLocationService
{
    public readonly struct TerritoryInfo
    {
        public int Index { get; init; }
        public float3 Center { get; init; }
        public string OwnerName { get; init; }
        public int CastleHeartIndex { get; init; }
    }

    // Tile coordinates store 2 units per world unit (see CastleTileOwnershipService.ToTilePosition).
    const float TileToWorld = 0.5f;

    public static IReadOnlyList<TerritoryInfo> GetTerritories()
    {
        var result = new List<TerritoryInfo>();
        if (!VRisingCore.IsReady)
            return result;

        var em = VRisingCore.EntityManager;
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<CastleTerritory>());
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Exists())
                    continue;

                var territory = em.GetComponentData<CastleTerritory>(entity);
                result.Add(new TerritoryInfo
                {
                    Index = entity.Index,
                    CastleHeartIndex = territory.CastleHeart.Index,
                    Center = ComputeCenter(em, territory),
                    OwnerName = ResolveOwnerName(em, territory)
                });
            }
        }
        finally
        {
            entities.Dispose();
            query.Dispose();
        }

        result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }

    public static bool TryGetTerritory(int id, out TerritoryInfo info)
    {
        foreach (var candidate in GetTerritories())
        {
            if (candidate.Index == id)
            {
                info = candidate;
                return true;
            }
        }

        info = default;
        return false;
    }

    static float3 ComputeCenter(EntityManager em, CastleTerritory territory)
    {
        var bounds = territory.WorldBounds;
        var cx = ((bounds.Min.x + bounds.Max.x) * 0.5f) * TileToWorld;
        var cz = ((bounds.Min.y + bounds.Max.y) * 0.5f) * TileToWorld;
        var cy = 0f;

        var heart = territory.CastleHeart;
        if (heart.Exists() && heart.Has<Translation>())
            cy = heart.Read<Translation>().Value.y;

        return new float3(cx, cy, cz);
    }

    static string ResolveOwnerName(EntityManager em, CastleTerritory territory)
    {
        var heart = territory.CastleHeart;
        if (!heart.Exists())
            return "Unowned";

        if (heart.Has<UserOwner>())
        {
            var owner = heart.Read<UserOwner>().Owner.GetEntityOnServer();
            if (owner.Exists() && owner.TryGetComponent(out User user))
                return user.CharacterName.ToString();
        }

        return $"Castle {heart.Index}";
    }
}
