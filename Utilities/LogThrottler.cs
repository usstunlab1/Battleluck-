using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BattleLuck.Utilities;

/// <summary>
/// Throttles and deduplicates repetitive log messages to prevent spam flooding.
/// Tracks message signatures, rate-limits, and provides diagnostics.
/// </summary>
public sealed class LogThrottler
{
    private sealed class ThrottleEntry
    {
        public string Signature { get; set; } = "";
        public int Count { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public long TotalCount { get; set; }
    }

    private static readonly object _lock = new();
    private readonly Dictionary<string, ThrottleEntry> _throttled = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _windowDuration;
    private readonly int _maxPerWindow;

    public LogThrottler(int maxPerWindow = 5, int windowSeconds = 5)
    {
        _maxPerWindow = maxPerWindow;
        _windowDuration = TimeSpan.FromSeconds(windowSeconds);
    }

    /// <summary>Check if a message should be logged or throttled.</summary>
    public (bool ShouldLog, string? Reason) ShouldThrottle(string message, string? category = null)
    {
        var signature = GetSignature(message, category);
        
        lock (_lock)
        {
            if (!_throttled.TryGetValue(signature, out var entry))
            {
                entry = new ThrottleEntry
                {
                    Signature = signature,
                    Count = 1,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    TotalCount = 1
                };
                _throttled[signature] = entry;
                return (true, null);
            }

            entry.LastSeen = DateTime.UtcNow;
            entry.TotalCount++;

            // Check if we're outside the window
            if (DateTime.UtcNow - entry.FirstSeen > _windowDuration)
            {
                // Reset window
                entry.FirstSeen = DateTime.UtcNow;
                entry.Count = 1;
                return (true, null);
            }

            // Within window
            entry.Count++;
            if (entry.Count <= _maxPerWindow)
            {
                return (true, null);
            }

            return (false, $"Throttled (seen {entry.Count}x in {_windowDuration.TotalSeconds}s, total {entry.TotalCount}x)");
        }
    }

    private static string GetSignature(string message, string? category)
    {
        // Extract core pattern (remove player names, numbers, etc. to group similar messages)
        var core = System.Text.RegularExpressions.Regex.Replace(message, @"\b[A-Za-z][a-z]+ (True|False)\b", "[PLAYER]");
        core = System.Text.RegularExpressions.Regex.Replace(core, @"\d+", "[NUM]");
        return $"{category}::{core}".GetHashCode().ToString();
    }

    public Dictionary<string, object> GetStats()
    {
        lock (_lock)
        {
            var active = _throttled.Count(e => DateTime.UtcNow - e.Value.LastSeen < TimeSpan.FromMinutes(1));
            var totalSupressed = _throttled.Sum(e => e.Value.TotalCount - e.Value.Count);
            
            return new()
            {
                { "signatures_tracked", _throttled.Count },
                { "active_last_minute", active },
                { "total_suppressed", totalSupressed }
            };
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _throttled.Clear();
        }
    }
}

/// <summary>Network event spam detector and blocker.</summary>
public sealed class NetworkEventSpamDetector
{
    private sealed class PlayerEventCount
    {
        public int CountLastSecond { get; set; }
        public DateTime LastCountReset { get; set; }
        public List<string> RecentEvents { get; set; } = new();
        public long TotalEvents { get; set; }
    }

    private static readonly object _lock = new();
    private readonly Dictionary<ulong, PlayerEventCount> _playerEvents = new();
    private readonly int _eventsPerSecondThreshold;
    private readonly int _recentWindowSize = 10;

    public NetworkEventSpamDetector(int eventsPerSecondThreshold = 20)
    {
        _eventsPerSecondThreshold = eventsPerSecondThreshold;
    }

    public (bool IsSpamming, int EventCount, List<string> RecentEvents) CheckPlayer(ulong steamId, string eventType)
    {
        lock (_lock)
        {
            if (!_playerEvents.TryGetValue(steamId, out var data))
            {
                data = new PlayerEventCount
                {
                    CountLastSecond = 1,
                    LastCountReset = DateTime.UtcNow,
                    TotalEvents = 1
                };
                data.RecentEvents.Add(eventType);
                _playerEvents[steamId] = data;
                return (false, 1, new());
            }

            // Check if we need to reset the counter
            if (DateTime.UtcNow - data.LastCountReset > TimeSpan.FromSeconds(1))
            {
                data.CountLastSecond = 1;
                data.LastCountReset = DateTime.UtcNow;
            }
            else
            {
                data.CountLastSecond++;
            }

            data.TotalEvents++;

            // Track recent events
            data.RecentEvents.Add(eventType);
            if (data.RecentEvents.Count > _recentWindowSize)
                data.RecentEvents.RemoveAt(0);

            var isSpamming = data.CountLastSecond > _eventsPerSecondThreshold;
            return (isSpamming, data.CountLastSecond, new List<string>(data.RecentEvents));
        }
    }

    public Dictionary<ulong, object> GetSpammers()
    {
        lock (_lock)
        {
            var result = new Dictionary<ulong, object>();
            foreach (var kvp in _playerEvents)
            {
                var data = kvp.Value;
                if (data.CountLastSecond > _eventsPerSecondThreshold / 2)
                {
                    result[kvp.Key] = new
                    {
                        eventsLastSecond = data.CountLastSecond,
                        totalEvents = data.TotalEvents,
                        recentEventTypes = string.Join(", ", data.RecentEvents.Distinct().Take(5))
                    };
                }
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _playerEvents.Clear();
        }
    }
}
