using System;
using Unity.Mathematics;

/// <summary>
/// Controls a shrinking zone boundary for battle-royale-style gameplay (Bloodbath).
/// Radius decreases over time. Supports dynamic waypoint movement with smooth lerp.
/// </summary>
public sealed class ShrinkZoneController
{
    public float InitialRadius { get; private set; }
    public float MinimumRadius { get; private set; }
    public float CurrentRadius { get; private set; }
    public float ShrinkRatePerSecond { get; private set; }
    public DateTime? StartedAt { get; private set; }

    public bool IsActive => StartedAt.HasValue && CurrentRadius > MinimumRadius;

    // ── Waypoint fields ─────────────────────────────────────────────
    float3[] _waypoints = Array.Empty<float3>();
    int _currentWaypointIndex;
    float _moveIntervalSec = 5f;
    float _moveSpeed = 8f;
    bool _loop = true;
    float _radiusShrinkPerWaypoint = 3f;
    float _waypointMinRadius = 15f;
    float _timeSinceLastMove;
    float3 _currentCenter;
    float3 _targetCenter;
    bool _waypointsEnabled;
    bool _frozen; // fail-fast freeze flag

    public float3 GetCurrentCenter() => _currentCenter;
    public bool WaypointsEnabled => _waypointsEnabled;
    public bool IsFrozen => _frozen;

    public void Configure(float initialRadius, float minimumRadius, float shrinkDurationSeconds)
    {
        InitialRadius = initialRadius;
        MinimumRadius = Math.Max(5f, minimumRadius);
        CurrentRadius = initialRadius;
        ShrinkRatePerSecond = shrinkDurationSeconds > 0
            ? (initialRadius - MinimumRadius) / shrinkDurationSeconds
            : 0;
    }

    public void ConfigureWaypoints(WaypointConfig? config, float3 initialCenter)
    {
        _currentCenter = initialCenter;
        _targetCenter = initialCenter;

        if (config == null || !config.Enabled || config.Points.Count == 0)
        {
            _waypointsEnabled = false;
            return;
        }

        _waypoints = config.Points.Select(p => p.ToFloat3()).ToArray();
        _moveIntervalSec = Math.Max(1f, config.MoveIntervalSec);
        _moveSpeed = Math.Max(0.5f, config.MoveSpeed);
        _loop = config.Loop;
        _radiusShrinkPerWaypoint = config.RadiusShrinkPerWaypoint;
        _waypointMinRadius = Math.Max(5f, config.MinimumRadius);
        _currentWaypointIndex = 0;
        _timeSinceLastMove = 0;
        _targetCenter = _waypoints[0];
        _waypointsEnabled = true;
        _frozen = false;
    }

    public void Start()
    {
        StartedAt = DateTime.UtcNow;
        CurrentRadius = InitialRadius;
        _timeSinceLastMove = 0;
        _frozen = false;
    }

    /// <summary>Tick the zone shrink. Returns true if the radius changed.</summary>
    public bool Tick(GameModeContext ctx, float deltaSeconds)
    {
        if (!IsActive || _frozen) return false;

        float oldRadius = CurrentRadius;
        CurrentRadius = Math.Max(MinimumRadius, CurrentRadius - ShrinkRatePerSecond * deltaSeconds);

        // Broadcast at significant thresholds (every 25% shrink)
        float totalRange = InitialRadius - MinimumRadius;
        if (totalRange > 0)
        {
            int oldPct = (int)((InitialRadius - oldRadius) / totalRange * 100);
            int newPct = (int)((InitialRadius - CurrentRadius) / totalRange * 100);
            if (newPct / 25 > oldPct / 25)
            {
                ctx.Broadcast?.Invoke($"🔴 Zone shrinking! Radius: {CurrentRadius:F0}m");
                GameEvents.OnZoneShrink?.Invoke(new ZoneShrinkEvent
                {
                    SessionId = ctx.SessionId,
                    OldRadius = oldRadius,
                    NewRadius = CurrentRadius
                });
            }
        }

        return Math.Abs(oldRadius - CurrentRadius) > 0.01f;
    }

    /// <summary>
    /// Tick waypoint movement. Returns true if center moved (triggers border/platform sync).
    /// Call AFTER Tick() each frame.
    /// </summary>
    public bool TickMovement(string sessionId, float deltaSeconds)
    {
        if (!_waypointsEnabled || !IsActive || _frozen) return false;

        try
        {
            // Smooth lerp toward target
            float dist = math.distance(_currentCenter.xz, _targetCenter.xz);
            if (dist > 0.05f)
            {
                float step = _moveSpeed * deltaSeconds;
                _currentCenter = math.lerp(_currentCenter, _targetCenter, math.min(1f, step / dist));
                return true;
            }

            // Arrived at target — wait for interval
            _timeSinceLastMove += deltaSeconds;
            if (_timeSinceLastMove < _moveIntervalSec) return false;

            // Advance to next waypoint
            _timeSinceLastMove = 0;
            _currentWaypointIndex++;

            if (_currentWaypointIndex >= _waypoints.Length)
            {
                if (_loop)
                    _currentWaypointIndex = 0;
                else
                {
                    _waypointsEnabled = false;
                    return false;
                }
            }

            _targetCenter = _waypoints[_currentWaypointIndex];

            // Shrink radius per waypoint
            CurrentRadius = Math.Max(_waypointMinRadius, CurrentRadius - _radiusShrinkPerWaypoint);

            GameEvents.OnRealityChanged?.Invoke(new RealityStateChanged
            {
                SessionId = sessionId,
                State = "waypoint_advance",
                Center = _currentCenter,
                Radius = CurrentRadius
            });

            return true;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ShrinkZone] Waypoint error — freezing arena: {ex.Message}");
            _frozen = true;
            return false;
        }
    }

    public void Reset()
    {
        CurrentRadius = InitialRadius;
        StartedAt = null;
        _waypointsEnabled = false;
        _frozen = false;
        _currentWaypointIndex = 0;
        _timeSinceLastMove = 0;
    }
}
