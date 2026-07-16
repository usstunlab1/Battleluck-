using System;
using System.Collections.Generic;
using System.Linq;
using BattleLuck.Models;

namespace BattleLuck.Services.Flow
{
    /// <summary>
    /// Manages runtime overrides for flow configurations.
    /// Allows live modification of flows without immediately writing to disk.
    /// </summary>
    public sealed class FlowOverrideManager
    {
        private static readonly Lazy<FlowOverrideManager> _instance = new(() => new FlowOverrideManager());
        public static FlowOverrideManager Instance => _instance.Value;

        private readonly Dictionary<string, FlowOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Stack<FlowOverride>> _undoStacks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        private FlowOverrideManager() { }

        /// <summary>
        /// Get the effective flow for a mode and flow type.
        /// Returns override if present, otherwise loads base from ConfigLoader.
        /// </summary>
        public FlowConfig GetEffectiveFlow(string modeId, FlowType flowType)
        {
            var key = GetKey(modeId, flowType);

            lock (_lock)
            {
                if (_overrides.TryGetValue(key, out var overrideData))
                {
                    return overrideData.Flow;
                }
            }

            // Load base flow from ConfigLoader
            var config = ConfigLoader.Load(modeId);
            return flowType == FlowType.Enter ? config.FlowEnter : config.FlowExit;
        }

        /// <summary>
        /// Set an override for a mode and flow type.
        /// Stores previous state in undo stack.
        /// </summary>
        public void SetOverride(string modeId, FlowType flowType, FlowConfig flow)
        {
            var key = GetKey(modeId, flowType);

            lock (_lock)
            {
                // Store current state in undo stack
                var current = GetEffectiveFlow(modeId, flowType);
                if (!_undoStacks.ContainsKey(key))
                {
                    _undoStacks[key] = new Stack<FlowOverride>();
                }
                _undoStacks[key].Push(new FlowOverride
                {
                    ModeId = modeId,
                    FlowType = flowType,
                    Flow = CloneFlowConfig(current),
                    Timestamp = DateTime.UtcNow
                });

                // Set new override
                _overrides[key] = new FlowOverride
                {
                    ModeId = modeId,
                    FlowType = flowType,
                    Flow = CloneFlowConfig(flow),
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Clear override for a mode and flow type, reverting to base config.
        /// </summary>
        public void ClearOverride(string modeId, FlowType flowType)
        {
            var key = GetKey(modeId, flowType);

            lock (_lock)
            {
                if (_overrides.Remove(key))
                {
                    BattleLuckPlugin.LogInfo($"[FlowOverrideManager] Cleared override for {modeId}/{flowType}");
                }
            }
        }

        /// <summary>
        /// Undo the last change for a mode and flow type.
        /// </summary>
        public bool Undo(string modeId, FlowType flowType)
        {
            var key = GetKey(modeId, flowType);

            lock (_lock)
            {
                if (!_undoStacks.ContainsKey(key) || _undoStacks[key].Count == 0)
                {
                    return false;
                }

                var previous = _undoStacks[key].Pop();
                _overrides[key] = previous;

                BattleLuckPlugin.LogInfo($"[FlowOverrideManager] Undid change for {modeId}/{flowType}");
                return true;
            }
        }

        /// <summary>
        /// Check if an override exists for a mode and flow type.
        /// </summary>
        public bool HasOverride(string modeId, FlowType flowType)
        {
            var key = GetKey(modeId, flowType);
            lock (_lock)
            {
                return _overrides.ContainsKey(key);
            }
        }

        /// <summary>
        /// Get all current overrides.
        /// </summary>
        public IReadOnlyDictionary<string, FlowOverride> GetAllOverrides()
        {
            lock (_lock)
            {
                return new Dictionary<string, FlowOverride>(_overrides, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Clear all overrides.
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                var count = _overrides.Count;
                _overrides.Clear();
                _undoStacks.Clear();
                BattleLuckPlugin.LogInfo($"[FlowOverrideManager] Cleared all overrides ({count} mode/flow combinations)");
            }
        }

        /// <summary>
        /// Get the base flow (without overrides) for a mode and flow type.
        /// </summary>
        public FlowConfig GetBaseFlow(string modeId, FlowType flowType)
        {
            var config = ConfigLoader.Load(modeId);
            return flowType == FlowType.Enter ? config.FlowEnter : config.FlowExit;
        }

        private static string GetKey(string modeId, FlowType flowType)
        {
            return $"{modeId}:{flowType.ToString().ToLowerInvariant()}";
        }

        private static FlowConfig CloneFlowConfig(FlowConfig original)
        {
            // Deep clone the flow config with null-safe handling
            return new FlowConfig
            {
                DelayBefore = CloneDelayConfig(original.DelayBefore),
                DelayBetweenFlows = CloneDelayConfig(original.DelayBetweenFlows),
                DelayBetweenActions = CloneDelayConfig(original.DelayBetweenActions),
                ExecutionOrder = original.ExecutionOrder != null ? new List<string>(original.ExecutionOrder) : new List<string>(),
                Flows = original.Flows != null ? new Dictionary<string, FlowDefinition>(original.Flows, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, FlowDefinition>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static DelayConfig CloneDelayConfig(DelayConfig original)
        {
            return new DelayConfig
            {
                Seconds = original.Seconds
            };
        }
    }

    /// <summary>
    /// Represents a flow override with metadata.
    /// </summary>
    public sealed class FlowOverride
    {
        public string ModeId { get; set; } = "";
        public FlowType FlowType { get; set; }
        public FlowConfig Flow { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Flow type enumeration.
    /// </summary>
    public enum FlowType
    {
        Enter,
        Exit
    }
}
