using BattleLuck.Models;
using ProjectM;
using Unity.Collections;
using Unity.Entities;

namespace BattleLuck.Services.Runtime;

public sealed record PlayerDirectoryEntry(
    string CharacterName, ulong SteamId, bool IsOnline, Entity UserEntity, Entity CharacterEntity);

/// <summary>
/// Safe KindredExtract-style player directory. Native collections are fully
/// materialized and disposed before managed projections leave the main thread.
/// </summary>
public sealed class PlayerDirectoryService
{
    readonly object _gate = new();
    readonly Dictionary<string, PlayerDirectoryEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ulong, PlayerDirectoryEntry> _bySteam = new();

    public int Count { get { lock (_gate) return _bySteam.Count; } }

    public void Rebuild(EntityManager entityManager, bool includeDisabled = false)
    {
        var byName = new Dictionary<string, PlayerDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        var bySteam = new Dictionary<ulong, PlayerDirectoryEntry>();
        var builder = new EntityQueryBuilder(Allocator.Temp).AddAll(ComponentType.ReadOnly<User>());
        if (includeDisabled) builder.WithOptions(EntityQueryOptions.IncludeDisabled);
        try
        {
            var query = entityManager.CreateEntityQuery(ref builder);
            try
            {
                var entities = query.ToEntityArray(Allocator.Temp);
                try
                {
                    for (var index = 0; index < entities.Length; index++)
                    {
                        var userEntity = entities[index];
                        if (!entityManager.Exists(userEntity) || !entityManager.HasComponent<User>(userEntity)) continue;
                        var user = entityManager.GetComponentData<User>(userEntity);
                        if (user.PlatformId == 0) continue;
                        var name = NormalizeName(user.CharacterName.ToString());
                        if (name.Length == 0) continue;
                        var character = user.LocalCharacter._Entity;
                        if (!entityManager.Exists(character)) character = Entity.Null;
                        var entry = new PlayerDirectoryEntry(user.CharacterName.ToString().Trim(), user.PlatformId,
                            user.IsConnected, userEntity, character);
                        byName[name] = entry;
                        bySteam[user.PlatformId] = entry;
                    }
                }
                finally { entities.Dispose(); }
            }
            finally { query.Dispose(); }
        }
        finally { builder.Dispose(); }

        lock (_gate)
        {
            _byName.Clear(); _bySteam.Clear();
            foreach (var pair in byName) _byName[pair.Key] = pair.Value;
            foreach (var pair in bySteam) _bySteam[pair.Key] = pair.Value;
        }
        BattleLuckPlugin.LogInfo($"[PlayerDirectory] Rebuilt {bySteam.Count} entries; online={bySteam.Values.Count(value => value.IsOnline)}.");
    }

    public bool TryFindSteam(ulong steamId, out PlayerDirectoryEntry entry)
    { lock (_gate) return _bySteam.TryGetValue(steamId, out entry!); }

    public bool TryFindName(string name, out PlayerDirectoryEntry entry)
    { lock (_gate) return _byName.TryGetValue(NormalizeName(name), out entry!); }

    public IReadOnlyList<PlayerProjection> GetOnlineProjections(ISet<ulong>? participants = null,
        IReadOnlyDictionary<ulong, int>? teams = null, int limit = 32)
    {
        lock (_gate)
        {
            return _bySteam.Values.Where(value => value.IsOnline).OrderBy(value => value.CharacterName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 128)).Select((value, index) => new PlayerProjection(
                    $"player-{index + 1}", value.CharacterName, true, value.CharacterEntity != Entity.Null,
                    participants?.Contains(value.SteamId) == true,
                    teams != null && teams.TryGetValue(value.SteamId, out var team) ? team : -1)).ToArray();
        }
    }

    public static string NormalizeName(string? name) => string.Join(' ', (name ?? "").Trim()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static bool IsValidRename(string? name, IEnumerable<string>? reservedNames, out string error)
    {
        var value = (name ?? "").Trim();
        if (value.Length is < 3 or > 32) { error = "Name length must be 3-32 characters."; return false; }
        if (value.Any(ch => char.IsControl(ch) || ch is '<' or '>' or '\r' or '\n'))
        { error = "Name contains control or rich-text characters."; return false; }
        if (reservedNames?.Any(item => NormalizeName(item) == NormalizeName(value)) == true)
        { error = "Name is reserved."; return false; }
        error = ""; return true;
    }
}
