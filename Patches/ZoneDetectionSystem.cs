using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Periodically checks player positions against configured zones
/// and fires enter/exit events when players cross zone boundaries.
/// Not a Harmony patch — uses a tick-based polling approach.
/// </summary>
public sealed class ZoneDetectionSystem
{
    readonly Dictionary<ulong, int> _playerZones = new(); // steamId → zoneHash (0 = not in zone)
    readonly Dictionary<ulong, float3> _lastOutsidePositions = new();
    readonly Dictionary<int, ZoneDefinition> _allZones = new();
    DateTime _lastCheck = DateTime.UtcNow;
    int _checkIntervalMs = 500;

    /// <summary>Raised when a player enters a zone. (steamId, playerEntity, zone)</summary>
    public event Action<ulong, Entity, ZoneDefinition>? OnPlayerEnterZone;

    /// <summary>Raised when a player exits a zone. (steamId, playerEntity, previousZoneHash)</summary>
    public event Action<ulong, Entity, int>? OnPlayerExitZone;

    public void Initialize()
    {
        _allZones.Clear();
        foreach (var modeId in new[] { "bloodbath", "siege", "trials", "colosseum", "aievent" })
        {
            var config = ConfigLoader.Load(modeId);
            _checkIntervalMs = config.Zones.Detection.CheckIntervalMs;

            foreach (var zone in config.Zones.Zones)
            {
                _allZones[zone.Hash] = zone;
            }
        }

        BattleLuckPlugin.LogInfo($"[ZoneDetection] Loaded {_allZones.Count} zones.");
    }

    /// <summary>Call from game loop tick. Checks all online players against all zones.</summary>
    public void Tick(IEnumerable<Entity> onlinePlayers)
    {
        if ((DateTime.UtcNow - _lastCheck).TotalMilliseconds < _checkIntervalMs) return;
        _lastCheck = DateTime.UtcNow;

        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer()) continue;

            ulong steamId = player.GetSteamId();
            if (steamId == 0) continue;

            float3 pos = player.GetPosition();
            int currentZone = _playerZones.GetValueOrDefault(steamId, 0);
            int detectedZone = DetectZone(pos);

            if (detectedZone == 0)
                _lastOutsidePositions[steamId] = pos;

            if (currentZone != detectedZone)
            {
                // Left a zone
                if (currentZone != 0)
                {
                    _playerZones[steamId] = 0;
                    OnPlayerExitZone?.Invoke(steamId, player, currentZone);
                }

                // Entered a zone
                if (detectedZone != 0 && _allZones.TryGetValue(detectedZone, out var zone))
                {
                    _playerZones[steamId] = detectedZone;
                    OnPlayerEnterZone?.Invoke(steamId, player, zone);
                }
            }
        }
    }

    int DetectZone(float3 position)
    {
        foreach (var kv in _allZones)
        {
            var zone = kv.Value;
            var zonePos = zone.Position.ToFloat3();
            float dist = math.distance(new float2(position.x, position.z), new float2(zonePos.x, zonePos.z));

            if (dist <= zone.Radius)
                return zone.Hash;
        }
        return 0;
    }

    /// <summary>Check if player is currently tracked in any zone.</summary>
    public int GetPlayerZone(ulong steamId) => _playerZones.GetValueOrDefault(steamId, 0);

    /// <summary>Manually set a player's zone (e.g., after teleport).</summary>
    public void SetPlayerZone(ulong steamId, int zoneHash) => _playerZones[steamId] = zoneHash;

    /// <summary>Last position observed while the player was outside every BattleLuck zone.</summary>
    public float3? GetLastOutsidePosition(ulong steamId) =>
        _lastOutsidePositions.TryGetValue(steamId, out var position) ? position : null;

    /// <summary>Remove player tracking (disconnect).</summary>
    public void RemovePlayer(ulong steamId)
    {
        _playerZones.Remove(steamId);
        _lastOutsidePositions.Remove(steamId);
    }
}
