using BattleLuck.Commands;

namespace BattleLuck.Commands
{
    /// <summary>
    /// Admin commands for live flow action management.
    /// </summary>
    public static class FlowCommands
    {
        [Command("flow.list", description: "List effective flow actions for a mode", adminOnly: true)]
        public static void FlowList(ChatCommandContext ctx, string modeId, string flowTypeStr)
        {
            if (!TryParseFlowType(flowTypeStr, out var flowType))
            {
                ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId) == null)
            {
                ctx.Reply($"Unknown mode: {modeId}");
                return;
            }

            var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowType);
            var hasOverride = FlowOverrideManager.Instance.HasOverride(modeId, flowType);

            ctx.Reply($"Flow actions for {modeId}/{flowTypeStr} {(hasOverride ? "(OVERRIDE)" : "(BASE)")}:");
            ctx.Reply($"  Execution order: {string.Join(", ", flow.ExecutionOrder)}");

            int actionIndex = 0;
            foreach (var flowName in flow.ExecutionOrder)
            {
                if (!flow.Flows.TryGetValue(flowName, out var flowDef))
                    continue;

                ctx.Reply($"  Flow: {flowName}");
                if (!string.IsNullOrEmpty(flowDef.Description))
                    ctx.Reply($"    Description: {flowDef.Description}");

                foreach (var action in flowDef.Actions)
                {
                    ctx.Reply($"    [{actionIndex}] {action}");
                    actionIndex++;
                }
            }

            ctx.Reply($"Total actions: {actionIndex}");
        }

        [Command("flow.scale", description: "Scale numeric parameters in flow actions", adminOnly: true)]
        public static void FlowScale(ChatCommandContext ctx, string modeId, string flowTypeStr, string indexOrFilter, string multiplierStr, string dryRunFlag = "")
        {
            if (!TryParseFlowType(flowTypeStr, out var flowType))
            {
                ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId) == null)
            {
                ctx.Reply($"Unknown mode: {modeId}");
                return;
            }

            if (!double.TryParse(multiplierStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
            {
                ctx.Reply($"Invalid multiplier: {multiplierStr}");
                return;
            }

            bool dryRun = dryRunFlag.Equals("--dry-run", StringComparison.OrdinalIgnoreCase);

            var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowType);
            var originalFlow = CloneFlowConfig(flow);

            // Parse index or filter
            bool isFilter = !int.TryParse(indexOrFilter, out int targetIndex);

            int scaledCount = 0;
            foreach (var flowName in flow.ExecutionOrder)
            {
                if (!flow.Flows.TryGetValue(flowName, out var flowDef))
                    continue;

                for (int i = 0; i < flowDef.Actions.Count; i++)
                {
                    var action = flowDef.Actions[i];
                    bool shouldScale = isFilter 
                        ? action.Contains(indexOrFilter, StringComparison.OrdinalIgnoreCase)
                        : i == targetIndex;

                    if (shouldScale)
                    {
                        var scaled = ScaleActionString(action, multiplier);
                        if (scaled != action)
                        {
                            if (!dryRun)
                            {
                                flowDef.Actions[i] = scaled;
                            }
                            ctx.Reply($"  [{i}] {action} → {scaled}");
                            scaledCount++;
                        }
                    }
                }
            }

            if (scaledCount == 0)
            {
                ctx.Reply($"No actions matched '{indexOrFilter}' or no numeric parameters to scale.");
                return;
            }

            if (!dryRun)
            {
                FlowOverrideManager.Instance.SetOverride(modeId, flowType, flow);
                ctx.Reply($"Applied {multiplier}x scaling to {scaledCount} action(s) in {modeId}/{flowTypeStr}.");
            }
            else
            {
                ctx.Reply($"[DRY-RUN] Would scale {scaledCount} action(s) by {multiplier}x in {modeId}/{flowTypeStr}.");
                ctx.Reply("Run without --dry-run to apply changes.");
            }
        }

        [Command("flow.move", description: "Move actions between modes or flows", adminOnly: true)]
        public static void FlowMove(ChatCommandContext ctx, string fromMode, string fromFlowTypeStr, string indexOrFilter, string toMode, string toFlowTypeStr, string positionStr = "")
        {
            if (!TryParseFlowType(fromFlowTypeStr, out var fromFlowType))
            {
                ctx.Reply($"Invalid from flow type: {fromFlowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            if (!TryParseFlowType(toFlowTypeStr, out var toFlowType))
            {
                ctx.Reply($"Invalid to flow type: {toFlowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(fromMode) == null)
            {
                ctx.Reply($"Unknown from mode: {fromMode}");
                return;
            }

            if (registry?.Resolve(toMode) == null)
            {
                ctx.Reply($"Unknown to mode: {toMode}");
                return;
            }

            int? position = null;
            if (!string.IsNullOrEmpty(positionStr) && int.TryParse(positionStr, out int pos))
            {
                position = pos;
            }

            var fromFlow = FlowOverrideManager.Instance.GetEffectiveFlow(fromMode, fromFlowType);
            var toFlow = FlowOverrideManager.Instance.GetEffectiveFlow(toMode, toFlowType);

            // Find actions to move
            var actionsToMove = new List<string>();
            bool isFilter = !int.TryParse(indexOrFilter, out int targetIndex);

            foreach (var flowName in fromFlow.ExecutionOrder)
            {
                if (!fromFlow.Flows.TryGetValue(flowName, out var flowDef))
                    continue;

                for (int i = 0; i < flowDef.Actions.Count; i++)
                {
                    var action = flowDef.Actions[i];
                    bool shouldMove = isFilter
                        ? action.Contains(indexOrFilter, StringComparison.OrdinalIgnoreCase)
                        : i == targetIndex;

                    if (shouldMove)
                    {
                        actionsToMove.Add(action);
                    }
                }
            }

            if (actionsToMove.Count == 0)
            {
                ctx.Reply($"No actions matched '{indexOrFilter}'.");
                return;
            }

            // Remove from source
            foreach (var flowName in fromFlow.ExecutionOrder)
            {
                if (!fromFlow.Flows.TryGetValue(flowName, out var flowDef))
                    continue;

                flowDef.Actions.RemoveAll(a => actionsToMove.Contains(a));
            }

            // Add to destination
            if (!toFlow.Flows.ContainsKey("moved_actions"))
            {
                toFlow.Flows["moved_actions"] = new FlowDefinition
                {
                    Description = "Actions moved from other modes/flows",
                    Actions = new List<string>()
                };
            }

            var targetFlow = toFlow.Flows["moved_actions"];
            if (position.HasValue && position.Value >= 0 && position.Value <= targetFlow.Actions.Count)
            {
                targetFlow.Actions.Insert(position.Value, actionsToMove[0]);
            }
            else
            {
                targetFlow.Actions.AddRange(actionsToMove);
            }

            if (!toFlow.ExecutionOrder.Contains("moved_actions"))
            {
                toFlow.ExecutionOrder.Add("moved_actions");
            }

            // Apply overrides
            FlowOverrideManager.Instance.SetOverride(fromMode, fromFlowType, fromFlow);
            FlowOverrideManager.Instance.SetOverride(toMode, toFlowType, toFlow);

            ctx.Reply($"Moved {actionsToMove.Count} action(s) from {fromMode}/{fromFlowTypeStr} to {toMode}/{toFlowTypeStr}.");
            foreach (var action in actionsToMove)
            {
                ctx.Reply($"  - {action}");
            }
        }

        [Command("flow.reorder", description: "Reorder actions within a flow", adminOnly: true)]
        public static void FlowReorder(ChatCommandContext ctx, string modeId, string flowTypeStr, string fromIndexStr, string toIndexStr)
        {
            if (!TryParseFlowType(flowTypeStr, out var flowType))
            {
                ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId) == null)
            {
                ctx.Reply($"Unknown mode: {modeId}");
                return;
            }

            if (!int.TryParse(fromIndexStr, out int fromIndex) || fromIndex < 0)
            {
                ctx.Reply($"Invalid from index: {fromIndexStr}");
                return;
            }

            if (!int.TryParse(toIndexStr, out int toIndex) || toIndex < 0)
            {
                ctx.Reply($"Invalid to index: {toIndexStr}");
                return;
            }

            var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowType);

            // Flatten all actions into a single list for reordering
            var allActions = new List<(string flowName, string action)>();
            foreach (var flowName in flow.ExecutionOrder)
            {
                if (!flow.Flows.TryGetValue(flowName, out var flowDef))
                    continue;

                foreach (var action in flowDef.Actions)
                {
                    allActions.Add((flowName, action));
                }
            }

            if (fromIndex >= allActions.Count || toIndex >= allActions.Count)
            {
                ctx.Reply($"Index out of range. Total actions: {allActions.Count}");
                return;
            }

            // Reorder
            var actionToMove = allActions[fromIndex];
            allActions.RemoveAt(fromIndex);
            allActions.Insert(toIndex, actionToMove);

            // Rebuild flow structure (simplified: put all in first flow)
            var firstFlowName = flow.ExecutionOrder.FirstOrDefault();
            if (string.IsNullOrEmpty(firstFlowName))
            {
                ctx.Reply("No flows found to reorder.");
                return;
            }

            flow.Flows[firstFlowName].Actions = allActions.Select(a => a.action).ToList();

            // Apply override
            FlowOverrideManager.Instance.SetOverride(modeId, flowType, flow);

            ctx.Reply($"Reordered action from index {fromIndex} to {toIndex} in {modeId}/{flowTypeStr}.");
            ctx.Reply($"  Moved: {actionToMove.action}");
        }

        [Command("flow.undo", description: "Undo last change for a mode/flow", adminOnly: true)]
        public static void FlowUndo(ChatCommandContext ctx, string modeId, string flowTypeStr)
        {
            if (!TryParseFlowType(flowTypeStr, out var flowType))
            {
                ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            if (FlowOverrideManager.Instance.Undo(modeId, flowType))
            {
                ctx.Reply($"Undid last change for {modeId}/{flowTypeStr}.");
            }
            else
            {
                ctx.Reply($"No undo history found for {modeId}/{flowTypeStr}.");
            }
        }

        [Command("flow.catalog", description: "List available flow actions from catalog", adminOnly: true)]
        public static void FlowCatalog(ChatCommandContext ctx, string filter = "")
        {
            var actions = FlowActionExecutor.SupportedActions;

            if (!string.IsNullOrEmpty(filter))
            {
                actions = actions.Where(a => a.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ctx.Reply($"Available flow actions ({actions.Count}):");
            foreach (var action in actions.OrderBy(a => a))
            {
                ctx.Reply($"  - {action}");
            }
        }

        [Command("flow.examples", description: "Show example action strings by category", adminOnly: true)]
        public static void FlowExamples(ChatCommandContext ctx, string category = "")
        {
            try
            {
                var catalogPath = System.IO.Path.Combine(ConfigLoader.ConfigRoot, "actions_catalog.json");
                if (!System.IO.File.Exists(catalogPath))
                {
                    ctx.Reply($"actions_catalog.json not found at {catalogPath}");
                    return;
                }

                var json = System.IO.File.ReadAllText(catalogPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("examples", out var examples))
                {
                    ctx.Reply("No examples found in actions_catalog.json");
                    return;
                }

                if (!string.IsNullOrEmpty(category))
                {
                    if (examples.TryGetProperty(category, out var categoryExamples))
                    {
                        ctx.Reply($"Examples for '{category}':");
                        foreach (var example in categoryExamples.EnumerateArray())
                        {
                            ctx.Reply($"  - {example.GetString()}");
                        }
                    }
                    else
                    {
                        ctx.Reply($"Category '{category}' not found. Available categories:");
                        foreach (var cat in examples.EnumerateObject())
                        {
                            ctx.Reply($"  - {cat.Name}");
                        }
                    }
                }
                else
                {
                    ctx.Reply("Available example categories:");
                    foreach (var cat in examples.EnumerateObject())
                    {
                        ctx.Reply($"  - {cat.Name} ({cat.Value.GetArrayLength()} examples)");
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Reply($"Error reading actions_catalog.json: {ex.Message}");
            }
        }

        [Command("prefabs.list", description: "List available prefabs matching a filter", adminOnly: true)]
        public static void PrefabsList(ChatCommandContext ctx, string filter = "")
        {
            var allPrefabs = PrefabHelper.GetAll();

            if (!string.IsNullOrEmpty(filter))
            {
                var filtered = allPrefabs.Where(kvp => kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                ctx.Reply($"Prefabs matching '{filter}' ({filtered.Count()}):");
                foreach (var kvp in filtered.Take(50))
                {
                    ctx.Reply($"  {kvp.Key}");
                }
                if (filtered.Count > 50)
                {
                    ctx.Reply($"  ... and {filtered.Count - 50} more");
                }
            }
            else
            {
                ctx.Reply($"Total prefabs registered: {allPrefabs.Count()}");
                ctx.Reply("Use a filter to search for specific prefabs.");
                ctx.Reply("Example: bl.prefabs.list Buff_");
            }
        }

        [Command("prefabs.buffs", description: "List available buff prefabs", adminOnly: true)]
        public static void PrefabsBuffs(ChatCommandContext ctx, string filter = "")
        {
            var allPrefabs = PrefabHelper.GetAll();
            var buffPrefabs = allPrefabs.Where(kvp => kvp.Key.StartsWith("Buff_", StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(filter))
            {
                buffPrefabs = buffPrefabs.Where(kvp => kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ctx.Reply($"Buff prefabs ({buffPrefabs.Count}):");
            foreach (var kvp in buffPrefabs.Take(50))
            {
                ctx.Reply($"  {kvp.Key}");
            }
            if (buffPrefabs.Count > 50)
            {
                ctx.Reply($"  ... and {buffPrefabs.Count - 50} more");
            }
        }

        [Command("prefabs.abilities", description: "List available ability prefabs", adminOnly: true)]
        public static void PrefabsAbilities(ChatCommandContext ctx, string filter = "")
        {
            var allPrefabs = PrefabHelper.GetAll();
            var abilityPrefabs = allPrefabs.Where(kvp => kvp.Key.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(filter))
            {
                abilityPrefabs = abilityPrefabs.Where(kvp => kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ctx.Reply($"Ability prefabs ({abilityPrefabs.Count}):");
            foreach (var kvp in abilityPrefabs.Take(50))
            {
                ctx.Reply($"  {kvp.Key}");
            }
            if (abilityPrefabs.Count > 50)
            {
                ctx.Reply($"  ... and {abilityPrefabs.Count - 50} more");
            }
        }

        [Command("flow.clear", description: "Clear all flow overrides", adminOnly: true)]
        public static void FlowClearAll(ChatCommandContext ctx)
        {
            FlowOverrideManager.Instance.ClearAll();
            ctx.Reply("Cleared all flow overrides.");
        }

        [Command("flow.persist", description: "Persist flow overrides to config files", adminOnly: true)]
        public static void FlowPersist(ChatCommandContext ctx, string modeId, string flowTypeStr = "", string allFlag = "")
        {
            bool persistAll = allFlag.Equals("--all", StringComparison.OrdinalIgnoreCase);

            if (persistAll)
            {
                var result = FlowPersistence.PersistAll();
                if (result.Success)
                {
                    ctx.Reply($"Persisted all flow overrides to config files.");
                    ctx.Reply($"Summary: {FlowPersistence.GetPendingOverridesSummary()}");
                }
                else
                {
                    ctx.Reply($"Failed to persist: {result.Error}");
                }
                return;
            }

            if (!string.IsNullOrEmpty(flowTypeStr))
            {
                // Persist specific mode/flow
                if (!TryParseFlowType(flowTypeStr, out var flowType))
                {
                    ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                    return;
                }

                var registry = BattleLuckPlugin.GameModes;
                if (registry?.Resolve(modeId) == null)
                {
                    ctx.Reply($"Unknown mode: {modeId}");
                    return;
                }

                if (!FlowOverrideManager.Instance.HasOverride(modeId, flowType))
                {
                    ctx.Reply($"No override exists for {modeId}/{flowTypeStr}.");
                    return;
                }

                var result = FlowPersistence.PersistFlow(modeId, flowType);
                if (result.Success)
                {
                    ctx.Reply($"Persisted {modeId}/{flowTypeStr} to session.json.");
                    ctx.Reply("Config reloaded.");
                }
                else
                {
                    ctx.Reply($"Failed to persist: {result.Error}");
                }
            }
            else
            {
                // Persist all flows for a mode
                var registry = BattleLuckPlugin.GameModes;
                if (registry?.Resolve(modeId) == null)
                {
                    ctx.Reply($"Unknown mode: {modeId}");
                    return;
                }

                if (!FlowPersistence.HasPendingOverrides(modeId))
                {
                    ctx.Reply($"No pending overrides for {modeId}.");
                    return;
                }

                var result = FlowPersistence.PersistMode(modeId);
                if (result.Success)
                {
                    ctx.Reply($"Persisted all flows for {modeId} to session.json.");
                    ctx.Reply("Config reloaded.");
                }
                else
                {
                    ctx.Reply($"Failed to persist: {result.Error}");
                }
            }
        }

        [Command("flow.status", description: "Show pending flow overrides", adminOnly: true)]
        public static void FlowStatus(ChatCommandContext ctx)
        {
            var overrides = FlowOverrideManager.Instance.GetAllOverrides();
            
            if (overrides.Count == 0)
            {
                ctx.Reply("No pending flow overrides.");
                return;
            }

            ctx.Reply($"Pending overrides ({overrides.Count}):");
            foreach (var kvp in overrides.OrderBy(k => k.Key))
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2)
                {
                    var timestamp = kvp.Value.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    ctx.Reply($"  {parts[0]}/{parts[1]} (modified: {timestamp})");
                }
            }

            ctx.Reply($"Summary: {FlowPersistence.GetPendingOverridesSummary()}");
            ctx.Reply("Use 'flow.persist <modeId>' to write changes to disk.");
        }

        [Command("flow.ai.propose", description: "Generate AI-proposed flow changes", adminOnly: true)]
        public static async Task FlowAiPropose(ChatCommandContext ctx, string modeId, string flowTypeStr, string description)
        {
            if (!TryParseFlowType(flowTypeStr, out var flowType))
            {
                ctx.Reply($"Invalid flow type: {flowTypeStr}. Use 'enter' or 'exit'.");
                return;
            }

            var registry = BattleLuckPlugin.GameModes;
            if (registry?.Resolve(modeId) == null)
            {
                ctx.Reply($"Unknown mode: {modeId}");
                return;
            }

            var aiAssistant = BattleLuckPlugin.AIAssistant;
            if (aiAssistant == null)
            {
                ctx.Reply("AI Assistant is not initialized.");
                return;
            }

            var flow = FlowOverrideManager.Instance.GetEffectiveFlow(modeId, flowType);
            var flowJson = System.Text.Json.JsonSerializer.Serialize(flow);

            var prompt = $"I need to modify the following flow configuration for {modeId}/{flowTypeStr}.\n" +
                         $"Current flow JSON:\n{flowJson}\n\n" +
                         $"Requested change: {description}\n\n" +
                         $"Please provide the modified flow JSON with the requested changes applied. " +
                         "Return ONLY the JSON, no other text.";

            try
            {
                ctx.Reply("Generating AI proposal...");
                var response = await aiAssistant.HandleDirectQuery(ctx.GetSenderCharacterEntity().GetSteamId(), prompt, source: "flow_command");
                if (string.IsNullOrWhiteSpace(response))
                {
                    ctx.Reply("AI returned an empty response.");
                    return;
                }

                // Try to parse as JSON
                if (TryParseJson(response, out var doc))
                {
                    using (doc)
                    {
                        var proposedFlow = System.Text.Json.JsonSerializer.Deserialize<FlowConfig>(response);
                        if (proposedFlow != null)
                        {
                            // Store proposal for later application
                            var proposalId = Guid.NewGuid().ToString("N")[..8];
                            _aiProposals[proposalId] = new AiFlowProposal
                            {
                                ProposalId = proposalId,
                                ModeId = modeId,
                                FlowType = flowType,
                                OriginalFlow = CloneFlowConfig(flow),
                                ProposedFlow = proposedFlow,
                                Description = description,
                                Timestamp = DateTime.UtcNow
                            };

                            ctx.Reply($"AI proposal generated (ID: {proposalId}):");
                            ctx.Reply($"Description: {description}");
                            ctx.Reply($"Use 'flow.ai.apply {proposalId} yes' to apply this change.");
                            ctx.Reply($"Use 'flow.ai.apply {proposalId} no' to discard.");

                            // Show a preview of changes
                            ctx.Reply("Preview of changes:");
                            ShowFlowDiff(ctx, flow, proposedFlow);
                        }
                        else
                        {
                            ctx.Reply("AI response was not valid flow configuration JSON.");
                            ctx.Reply($"Response: {response}");
                        }
                    }
                }
                else
                {
                    ctx.Reply("AI response was not valid JSON.");
                    ctx.Reply($"Response: {response}");
                }
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogError($"[FlowCommands] AI proposal failed: {ex.Message}");
                ctx.Reply($"AI proposal failed: {ex.Message}");
            }
        }

        [Command("flow.ai.apply", description: "Apply or discard AI-proposed flow changes", adminOnly: true)]
        public static void FlowAiApply(ChatCommandContext ctx, string proposalId, string decision)
        {
            if (!_aiProposals.TryGetValue(proposalId, out var proposal))
            {
                ctx.Reply($"Proposal not found: {proposalId}");
                return;
            }

            if (decision.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                // Apply the proposal
                FlowOverrideManager.Instance.SetOverride(proposal.ModeId, proposal.FlowType, proposal.ProposedFlow);
                ctx.Reply($"Applied AI proposal {proposalId} to {proposal.ModeId}/{proposal.FlowType}.");
                ctx.Reply("Use 'flow.persist' to write changes to disk.");
            }
            else if (decision.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                // Discard the proposal
                _aiProposals.Remove(proposalId);
                ctx.Reply($"Discarded AI proposal {proposalId}.");
            }
            else
            {
                ctx.Reply($"Invalid decision: {decision}. Use 'yes' or 'no'.");
            }
        }

        private static void ShowFlowDiff(ChatCommandContext ctx, FlowConfig original, FlowConfig proposed)
        {
            // Show execution order changes
            if (!original.ExecutionOrder.SequenceEqual(proposed.ExecutionOrder))
            {
                ctx.Reply("  Execution order changed:");
                ctx.Reply($"    Before: {string.Join(", ", original.ExecutionOrder)}");
                ctx.Reply($"    After: {string.Join(", ", proposed.ExecutionOrder)}");
            }

            // Show flow count changes
            if (original.Flows.Count != proposed.Flows.Count)
            {
                ctx.Reply($"  Flow count: {original.Flows.Count} → {proposed.Flows.Count}");
            }

            // Show action count changes
            int originalActions = original.Flows.Values.Sum(f => f.Actions.Count);
            int proposedActions = proposed.Flows.Values.Sum(f => f.Actions.Count);
            if (originalActions != proposedActions)
            {
                ctx.Reply($"  Total actions: {originalActions} → {proposedActions}");
            }
        }

        private static readonly Dictionary<string, AiFlowProposal> _aiProposals = new();

        private sealed class AiFlowProposal
        {
            public string ProposalId { get; set; } = "";
            public string ModeId { get; set; } = "";
            public FlowType FlowType { get; set; }
            public FlowConfig OriginalFlow { get; set; } = new();
            public FlowConfig ProposedFlow { get; set; } = new();
            public string Description { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        private static bool TryParseFlowType(string flowTypeStr, out FlowType flowType)
        {
            flowType = flowTypeStr.Equals("enter", StringComparison.OrdinalIgnoreCase)
                ? FlowType.Enter
                : flowTypeStr.Equals("exit", StringComparison.OrdinalIgnoreCase)
                    ? FlowType.Exit
                    : FlowType.Enter; // Default
            return flowTypeStr.Equals("enter", StringComparison.OrdinalIgnoreCase) ||
                   flowTypeStr.Equals("exit", StringComparison.OrdinalIgnoreCase);
        }

        private static string ScaleActionString(string action, double multiplier)
        {
            if (!action.Contains(':'))
                return action;

            var parts = action.Split(':', 2);
            var actionName = parts[0];
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
                return action;

            var paramStr = parts[1];
            var pairs = paramStr.Split('|');
            var newPairs = new List<string>();

            var denyList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "prefabGUID", "guid", "zoneHash", "netId", "destNetId", "sourceNetId",
                "modeId", "kitId", "targetZoneHash", "timerId", "objectiveId",
                "buffPrefab", "sequencePrefab", "wallType", "floorType", "trapType",
                "mountType", "condition", "behavior", "formation", "message", "color",
                "type", "reason", "onComplete", "onTrue", "onFalse", "damageType",
                "bloodType", "prefab", "kit", "snapshotScope", "position"
            };

            var numericParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "seconds", "durationSeconds", "count", "delay", "interval", "radius",
                "amount", "value", "multiplier", "chance", "cooldown", "damage",
                "heal", "speed", "range", "angle", "force", "mass", "health",
                "maxHealth", "armor", "power", "level", "tier", "quality",
                "quantity", "size", "scale", "offset", "x", "y", "z",
                "duration", "width", "length", "height", "spacing", "followRange",
                "leashRange", "aggroRange", "captureTime", "rewardPoints", "stackCount",
                "maxGearLevel", "maxLives", "targetRadius", "shrinkRate", "warningDuration"
            };

            foreach (var pair in pairs)
            {
                if (!pair.Contains('='))
                {
                    newPairs.Add(pair);
                    continue;
                }

                var kv = pair.Split('=', 2);
                var key = kv[0].Trim();
                var val = kv[1].Trim();

                if (denyList.Contains(key))
                {
                    newPairs.Add(pair);
                    continue;
                }

                if (numericParams.Contains(key) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var numVal))
                {
                    var scaled = numVal * multiplier;
                    var scaledStr = scaled.ToString("0.##", CultureInfo.InvariantCulture);
                    newPairs.Add($"{key}={scaledStr}");
                }
                else
                {
                    newPairs.Add(pair);
                }
            }

            return $"{actionName}:{string.Join("|", newPairs)}";
        }

        private static FlowConfig CloneFlowConfig(FlowConfig original)
        {
            return new FlowConfig
            {
                DelayBefore = new DelayConfig { Seconds = original.DelayBefore.Seconds },
                DelayBetweenFlows = new DelayConfig { Seconds = original.DelayBetweenFlows.Seconds },
                DelayBetweenActions = new DelayConfig { Seconds = original.DelayBetweenActions.Seconds },
                ExecutionOrder = original.ExecutionOrder != null ? new List<string>(original.ExecutionOrder) : new List<string>(),
                Flows = original.Flows != null ? new Dictionary<string, FlowDefinition>(original.Flows, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, FlowDefinition>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static bool TryParseJson(string json, out System.Text.Json.JsonDocument doc)
        {
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                doc = null!;
                return false;
            }
        }
    }
}
