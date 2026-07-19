using System.Globalization;
using System.Text.Json;
using BattleLuck.Commands.Converters;
using BattleLuck.Models;
using BattleLuck.Services.Flow;
using BattleLuck.Services.Runtime;

namespace BattleLuck.Services.Runtime;

public sealed class CustomSequenceService
{
    public const int MaxActionsPerSequence = 1000;
    static readonly JsonSerializerOptions WriteOptions = new(ConfigLoader.JsonOptions) { WriteIndented = true };
    readonly ActionManifestService _manifest = new();

    public string ConfigPath => Path.Combine(ConfigLoader.ConfigRoot, "custom_sequences.json");

    public OperationResult<CustomSequencesConfig> Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigLoader.ConfigRoot);
            if (!File.Exists(ConfigPath))
                return OperationResult<CustomSequencesConfig>.Ok(new CustomSequencesConfig());

            var config = JsonSerializer.Deserialize<CustomSequencesConfig>(File.ReadAllText(ConfigPath), ConfigLoader.JsonOptions)
                         ?? new CustomSequencesConfig();
            Normalize(config);
            return OperationResult<CustomSequencesConfig>.Ok(config);
        }
        catch (Exception ex)
        {
            return OperationResult<CustomSequencesConfig>.Fail($"Failed to load custom_sequences.json: {ex.Message}");
        }
    }

    public OperationResult Save(CustomSequencesConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigLoader.ConfigRoot);
            Normalize(config);
            config.UpdatedUtc = DateTime.UtcNow;

            if (File.Exists(ConfigPath))
            {
                var backupPath = $"{ConfigPath}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                File.Copy(ConfigPath, backupPath, overwrite: true);
            }

            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, WriteOptions));
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to save custom_sequences.json: {ex.Message}");
        }
    }

    public OperationResult<CustomSequenceDefinition> Get(string sequenceId)
    {
        var load = Load();
        if (!load.Success || load.Value == null)
            return OperationResult<CustomSequenceDefinition>.Fail(load.Error ?? "Could not load custom sequences.");

        var id = NormalizeId(sequenceId);
        return load.Value.Sequences.TryGetValue(id, out var sequence)
            ? OperationResult<CustomSequenceDefinition>.Ok(sequence)
            : OperationResult<CustomSequenceDefinition>.Fail($"Custom sequence '{sequenceId}' was not found.");
    }

    public OperationResult<CustomSequenceDefinition> UpsertFromText(
        string sequenceId,
        string stepText,
        ulong requestedBy,
        string modeId = "",
        string description = "")
    {
        var id = NormalizeId(sequenceId);
        if (string.IsNullOrWhiteSpace(id))
            return OperationResult<CustomSequenceDefinition>.Fail("Sequence id is required.");

        var steps = ParseSteps(stepText);
        if (steps.Count == 0)
            return OperationResult<CustomSequenceDefinition>.Fail("No actions or timing steps were provided.");

        var load = Load();
        if (!load.Success || load.Value == null)
            return OperationResult<CustomSequenceDefinition>.Fail(load.Error ?? "Could not load custom sequences.");

        var config = load.Value;
        var now = DateTime.UtcNow;
        var existed = config.Sequences.TryGetValue(id, out var existing);
        var sequence = existing ?? new CustomSequenceDefinition
        {
            Id = id,
            DisplayName = id,
            CreatedBy = requestedBy.ToString(CultureInfo.InvariantCulture),
            CreatedUtc = now
        };

        sequence.Id = id;
        sequence.DisplayName = string.IsNullOrWhiteSpace(sequence.DisplayName) ? id : sequence.DisplayName;
        sequence.Description = string.IsNullOrWhiteSpace(description) ? sequence.Description : description.Trim();
        sequence.ModeId = string.IsNullOrWhiteSpace(modeId) ? sequence.ModeId : modeId.Trim();
        sequence.UpdatedUtc = now;
        sequence.Tags = sequence.Tags
            .Concat(new[] { "custom", "ai-ready" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        sequence.Steps = steps;

        var validation = Validate(sequence);
        if (!validation.Success)
            return OperationResult<CustomSequenceDefinition>.Fail(validation.Error ?? "Sequence validation failed.");

        config.Sequences[id] = sequence;
        var save = Save(config);
        return save.Success
            ? OperationResult<CustomSequenceDefinition>.Ok(sequence)
            : OperationResult<CustomSequenceDefinition>.Fail(save.Error ?? "Sequence save failed.");
    }

    public OperationResult<CustomSequenceDefinition> GatherFromCatalog(
        string sequenceId,
        string request,
        ulong requestedBy,
        string modeId = "",
        int maxActions = 12)
    {
        var id = NormalizeId(sequenceId);
        if (string.IsNullOrWhiteSpace(id))
            return OperationResult<CustomSequenceDefinition>.Fail("Sequence id is required.");
        if (string.IsNullOrWhiteSpace(request))
            return OperationResult<CustomSequenceDefinition>.Fail("Catalog request/search text is required.");

        var steps = BuildGatheredSteps(request, Math.Clamp(maxActions, 1, MaxActionsPerSequence));
        if (steps.Count == 0)
            return OperationResult<CustomSequenceDefinition>.Fail("No valid catalog actions matched that request.");

        var stepText = string.Join("; ", steps.Select(RenderStepText));
        return UpsertFromText(id, stepText, requestedBy, modeId,
            $"Gathered from actions_catalog.json for: {request.Trim()}");
    }

    public OperationResult<CustomSequenceDefinition> AppendFromText(string sequenceId, string stepText)
    {
        var load = Load();
        if (!load.Success || load.Value == null)
            return OperationResult<CustomSequenceDefinition>.Fail(load.Error ?? "Could not load custom sequences.");

        var id = NormalizeId(sequenceId);
        if (!load.Value.Sequences.TryGetValue(id, out var sequence))
            return OperationResult<CustomSequenceDefinition>.Fail($"Custom sequence '{sequenceId}' was not found.");

        var steps = ParseSteps(stepText);
        if (steps.Count == 0)
            return OperationResult<CustomSequenceDefinition>.Fail("No actions or timing steps were provided.");

        sequence.Steps.AddRange(steps);
        sequence.UpdatedUtc = DateTime.UtcNow;

        var validation = Validate(sequence);
        if (!validation.Success)
            return OperationResult<CustomSequenceDefinition>.Fail(validation.Error ?? "Sequence validation failed.");

        var save = Save(load.Value);
        return save.Success
            ? OperationResult<CustomSequenceDefinition>.Ok(sequence)
            : OperationResult<CustomSequenceDefinition>.Fail(save.Error ?? "Sequence save failed.");
    }

    public OperationResult Delete(string sequenceId)
    {
        var load = Load();
        if (!load.Success || load.Value == null)
            return OperationResult.Fail(load.Error ?? "Could not load custom sequences.");

        var id = NormalizeId(sequenceId);
        if (!load.Value.Sequences.Remove(id))
            return OperationResult.Fail($"Custom sequence '{sequenceId}' was not found.");

        return Save(load.Value);
    }

    public List<CustomSequenceSummary> List()
    {
        var load = Load();
        if (!load.Success || load.Value == null)
            return new List<CustomSequenceSummary>();

        return load.Value.Sequences.Values
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(s => new CustomSequenceSummary
            {
                Id = s.Id,
                DisplayName = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Id : s.DisplayName,
                Description = s.Description,
                ModeId = s.ModeId,
                Actions = s.EnabledActionCount,
                Steps = s.Steps.Count,
                HasTiming = s.HasTiming,
                RiskLevel = s.RiskLevel
            })
            .ToList();
    }

    public OperationResult Validate(CustomSequenceDefinition sequence)
    {
        Normalize(sequence);
        var actionCount = sequence.EnabledActionCount;
        if (actionCount > MaxActionsPerSequence)
            return OperationResult.Fail($"Sequence '{sequence.Id}' has {actionCount} expanded actions; maximum is {MaxActionsPerSequence}.");

        var errors = new List<string>();
        foreach (var step in sequence.Steps.Where(s => s.Enabled && s.Kind.Equals("action", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(step.Action))
            {
                errors.Add($"{step.StepId}: action is empty");
                continue;
            }

            if (IsCustomSequenceAction(step.Action))
            {
                errors.Add($"{step.StepId}: nested custom sequence actions are disabled in V1");
                continue;
            }

            var validation = _manifest.Validate(new EventActionDefinition { Action = step.Action });
            if (!validation.Success)
                errors.Add($"{step.StepId}: {validation.Error}");
        }

        return errors.Count == 0
            ? OperationResult.Ok()
            : OperationResult.Fail(string.Join("; ", errors.Take(6)));
    }

    public OperationResult<CustomSequenceExecutionReport> ExecuteImmediate(
        string sequenceId,
        IActionRuntime executor,
        FlowActionContext context)
    {
        var get = Get(sequenceId);
        if (!get.Success || get.Value == null)
            return OperationResult<CustomSequenceExecutionReport>.Fail(get.Error ?? "Sequence not found.");

        var validation = Validate(get.Value);
        if (!validation.Success)
            return OperationResult<CustomSequenceExecutionReport>.Fail(validation.Error ?? "Sequence validation failed.");

        var report = new CustomSequenceExecutionReport { SequenceId = get.Value.Id };
        var rtContext = ToRuntimeContext(context);
        foreach (var step in get.Value.Steps.Where(s => s.Enabled))
        {
            if (!step.Kind.Equals("action", StringComparison.OrdinalIgnoreCase))
            {
                report.SkippedTimingMarkers++;
                continue;
            }

            var repeat = Math.Clamp(step.Repeat, 1, MaxActionsPerSequence);
            for (var i = 0; i < repeat; i++)
            {
                var intent = new RuntimeActionIntent { ActionName = step.Action };
                var result = executor.Execute(intent, rtContext);
                if (result.IsSuccess)
                {
                    report.Executed++;
                }
                else
                {
                    report.Failed++;
                    report.Errors.Add($"{step.StepId}: {result.Error}");
                }
            }
        }

        return report.Failed == 0
            ? OperationResult<CustomSequenceExecutionReport>.Ok(report)
            : OperationResult<CustomSequenceExecutionReport>.Fail(string.Join("; ", report.Errors.Take(4)));
    }

    /// <summary>
    /// Adapter: the deprecated <see cref="FlowActionContext"/> used by callers into
    /// <see cref="ExecuteImmediate"/> is projected onto the canonical
    /// <see cref="RuntimeActionContext"/> consumed by <see cref="IActionRuntime"/>.
    /// </summary>
    private static RuntimeActionContext ToRuntimeContext(FlowActionContext context)
    {
        return new RuntimeActionContext
        {
            PlayerCharacter = context.PlayerCharacter,
            ZoneHash = context.ZoneHash,
            PlayerState = context.PlayerState,
            Registry = context.Registry,
            ModeConfig = context.Config,
            Zone = context.Zone,
            SessionContext = context.GameContext,
        };
    }

    public OperationResult<CustomSequenceRuntimeRun> BuildRuntimeRun(string sequenceId, double startElapsedSeconds, string reason)
    {
        var get = Get(sequenceId);
        if (!get.Success || get.Value == null)
            return OperationResult<CustomSequenceRuntimeRun>.Fail(get.Error ?? "Sequence not found.");

        var validation = Validate(get.Value);
        if (!validation.Success)
            return OperationResult<CustomSequenceRuntimeRun>.Fail(validation.Error ?? "Sequence validation failed.");

        var cursor = startElapsedSeconds;
        var run = new CustomSequenceRuntimeRun
        {
            RunId = $"seq_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..24],
            SequenceId = get.Value.Id,
            Reason = reason,
            StartedAtElapsedSeconds = startElapsedSeconds
        };

        foreach (var step in get.Value.Steps.Where(s => s.Enabled))
        {
            if (step.Kind.Equals("wait", StringComparison.OrdinalIgnoreCase))
            {
                cursor += Math.Max(0, step.DelaySeconds);
                continue;
            }

            if (step.Kind.Equals("tick", StringComparison.OrdinalIgnoreCase))
            {
                if (step.AtSecond.HasValue)
                    cursor = Math.Max(cursor, step.AtSecond.Value);
                else
                    cursor += Math.Max(0, step.DelaySeconds);
                continue;
            }

            if (!step.Kind.Equals("action", StringComparison.OrdinalIgnoreCase))
                continue;

                var due = step.AtSecond ?? (cursor + Math.Max(0, step.DelaySeconds));
                var repeat = Math.Clamp(step.Repeat, 1, MaxActionsPerSequence);
                for (var i = 0; i < repeat; i++)
                {
                    run.Steps.Add(new CustomSequenceRuntimeStep
                    {
                        StepId = repeat == 1 ? step.StepId : $"{step.StepId}.{i + 1}",
                        Action = step.Action,
                        DueElapsedSeconds = due + (Math.Max(0, step.IntervalSeconds) * i),
                        StepIndex = run.Steps.Count,
                        StepLabel = step.StepId
                    });
                }

            cursor = Math.Max(cursor, due + (Math.Max(0, step.IntervalSeconds) * Math.Max(0, repeat - 1)));
        }

        return run.Steps.Count == 0
            ? OperationResult<CustomSequenceRuntimeRun>.Fail($"Sequence '{sequenceId}' has no executable action steps.")
            : OperationResult<CustomSequenceRuntimeRun>.Ok(run);
    }

    public bool TryReadCustomSequenceAction(string actionString, out string sequenceId, out bool schedule, out bool preview)
    {
        sequenceId = "";
        schedule = true;
        preview = false;

        var (name, parameters) = FlowActionExecutor.ParseActionString(actionString ?? "");
        var normalized = ActionParameterConverter.Normalize(name, parameters);
        name = normalized.ActionName;
        parameters = normalized.Parameters;

        if (!IsCustomSequenceAction(name))
            return false;

        sequenceId = First(parameters, "sequenceId", "id", "name", "sequence");
        schedule = Bool(parameters, true, "schedule", "timed", "queue");
        preview = name.EndsWith(".preview", StringComparison.OrdinalIgnoreCase) || Bool(parameters, false, "preview");
        return true;
    }

    public List<CustomSequenceStep> ParseSteps(string stepText)
    {
        var steps = new List<CustomSequenceStep>();
        foreach (var token in SplitStepText(stepText))
        {
            if (TryParseTimingStep(token, out var timingStep))
            {
                steps.Add(timingStep);
                continue;
            }

            var action = token.StartsWith("action:", StringComparison.OrdinalIgnoreCase)
                ? token["action:".Length..].Trim()
                : token.Trim();

            action = ActionParameterConverter.NormalizeActionString(action);
            if (string.IsNullOrWhiteSpace(action))
                continue;

            steps.Add(new CustomSequenceStep
            {
                Kind = "action",
                Action = action,
                Repeat = 1,
                Enabled = true
            });
        }

        AssignStepIds(steps);
        return steps;
    }

    List<CustomSequenceStep> BuildGatheredSteps(string request, int maxActions)
    {
        var lower = request.ToLowerInvariant();
        var steps = new List<CustomSequenceStep>();
        var seenActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var timing in ExtractTimingMarkers(request).Take(20))
            steps.Add(timing);

        void AddAction(string action)
        {
            if (steps.Count(s => s.Kind.Equals("action", StringComparison.OrdinalIgnoreCase)) >= maxActions)
                return;
            if (string.IsNullOrWhiteSpace(action))
                return;

            var normalized = ActionParameterConverter.NormalizeActionString(action);
            if (string.IsNullOrWhiteSpace(normalized) || !seenActions.Add(normalized))
                return;

            var validation = _manifest.Validate(new EventActionDefinition { Action = normalized });
            if (validation.Success)
                steps.Add(new CustomSequenceStep { Kind = "action", Action = normalized, Repeat = 1, Enabled = true });
        }

        foreach (var explicitAction in ExtractExplicitActionStrings(request))
            AddAction(explicitAction);

        foreach (var action in BuildIntentActions(lower))
            AddAction(action);

        foreach (var match in _manifest.Search(request, Math.Max(12, maxActions * 2)))
        {
            foreach (var example in match.Examples.DefaultIfEmpty(match.Name))
                AddAction(example);
        }

        AssignStepIds(steps);
        return steps;
    }

    static IEnumerable<CustomSequenceStep> ExtractTimingMarkers(string request)
    {
        foreach (var token in SplitStepText(request.Replace(",", ";")))
        {
            if (TryParseTimingStep(token, out var step))
                yield return step;
        }
    }

    IEnumerable<string> ExtractExplicitActionStrings(string request)
    {
        foreach (var token in SplitStepText(request.Replace(",", ";")))
        {
            if (!token.Contains(':'))
                continue;

            var (name, _) = FlowActionExecutor.ParseActionString(token);
            if (_manifest.Entries.ContainsKey(name))
                yield return token;
        }
    }

    static IEnumerable<string> BuildIntentActions(string lower)
    {
        if (ContainsAny(lower, "intro", "start", "countdown", "announce", "message"))
            yield return "announce:title=BattleLuck|message=Sequence started.|color=#5CC8FF|level=info";

        if (ContainsAny(lower, "ready", "prepare", "warmup"))
            yield return "announce:title=Ready|message=Prepare for the next phase.|color=#FFD166|level=warning";

        if (ContainsAny(lower, "stun", "freeze"))
            yield return "player.stun:duration=3";

        if (ContainsAny(lower, "slow"))
            yield return "player.buff.apply:buffPrefab=Buff_General_Slow|duration=10";

        if (ContainsAny(lower, "boss", "vblood"))
            yield return "npc.spawn:prefab=CHAR_Manticore_VBlood|npcId=boss1|position=-2000,5,-2800|homeRadius=40";

        if (ContainsAny(lower, "glow", "light", "fx"))
            yield return "glow.enable:color=#66E3FF|duration=30";

        if (ContainsAny(lower, "border", "wall", "arena wall"))
            yield return "schematic.load:eventName=castle_design_template|safetyMode=event_tracked_zone_only";

        if (ContainsAny(lower, "timer", "time"))
            yield return "timer.start:timerId=sequence|duration=30";

        if (ContainsAny(lower, "cleanup", "end", "finish"))
            yield return "announce:title=Cleanup|message=Sequence cleanup started.|color=#FFD166|level=warning";
    }

    static string RenderStepText(CustomSequenceStep step)
    {
        if (step.Kind.Equals("wait", StringComparison.OrdinalIgnoreCase))
            return $"wait:{step.DelaySeconds.ToString("0.###", CultureInfo.InvariantCulture)}";
        if (step.Kind.Equals("tick", StringComparison.OrdinalIgnoreCase))
            return $"tick:{step.AtSecond.GetValueOrDefault(step.DelaySeconds).ToString("0.###", CultureInfo.InvariantCulture)}";
        if (step.Kind.Equals("note", StringComparison.OrdinalIgnoreCase))
            return $"note:{step.Note}";
        return step.Action;
    }

    public string RenderPreview(CustomSequenceDefinition sequence, int maxSteps = 12)
    {
        Normalize(sequence);
        var lines = new List<string>
        {
            $"{sequence.Id}: {sequence.EnabledActionCount} action(s), {sequence.Steps.Count} step(s), timing={(sequence.HasTiming ? "yes" : "no")}, risk={sequence.RiskLevel}"
        };

        foreach (var step in sequence.Steps.Take(maxSteps))
        {
            if (step.Kind.Equals("action", StringComparison.OrdinalIgnoreCase))
            {
                var extra = step.Repeat > 1 ? $" x{step.Repeat}" : "";
                if (step.DelaySeconds > 0)
                    extra += $" after {step.DelaySeconds:0.#}s";
                if (step.AtSecond.HasValue)
                    extra += $" at {step.AtSecond.Value:0.#}s";
                lines.Add($"- {step.StepId} action{extra}: {step.Action}");
            }
            else if (step.Kind.Equals("wait", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"- {step.StepId} wait {step.DelaySeconds:0.#}s");
            }
            else if (step.Kind.Equals("tick", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"- {step.StepId} tick at {step.AtSecond.GetValueOrDefault(step.DelaySeconds):0.#}s");
            }
            else
            {
                lines.Add($"- {step.StepId} {step.Kind}: {step.Note}");
            }
        }

        if (sequence.Steps.Count > maxSteps)
            lines.Add($"... {sequence.Steps.Count - maxSteps} more step(s)");
        return string.Join("\n", lines);
    }

    static IEnumerable<string> SplitStepText(string value) =>
        (value ?? "")
            .Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s));

    static bool TryParseTimingStep(string token, out CustomSequenceStep step)
    {
        step = new CustomSequenceStep();
        var trimmed = token.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (TryReadPrefixedNumber(trimmed, lower, new[] { "wait", "delay", "sleep" }, out var waitSeconds))
        {
            step = new CustomSequenceStep
            {
                Kind = "wait",
                DelaySeconds = Math.Clamp(waitSeconds, 0, 86400),
                Enabled = true
            };
            return true;
        }

        if (TryReadPrefixedNumber(trimmed, lower, new[] { "tick", "at", "second" }, out var atSecond))
        {
            step = new CustomSequenceStep
            {
                Kind = "tick",
                AtSecond = Math.Clamp(atSecond, 0, 86400),
                Enabled = true
            };
            return true;
        }

        if (lower.StartsWith("note:", StringComparison.Ordinal) || lower.StartsWith("comment:", StringComparison.Ordinal))
        {
            var index = trimmed.IndexOf(':');
            step = new CustomSequenceStep
            {
                Kind = "note",
                Note = index >= 0 ? trimmed[(index + 1)..].Trim() : trimmed,
                Enabled = true
            };
            return true;
        }

        return false;
    }

    static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    static bool TryReadPrefixedNumber(string trimmed, string lower, string[] prefixes, out double value)
    {
        value = 0;
        foreach (var prefix in prefixes)
        {
            if (!lower.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                !lower.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase) &&
                !lower.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase) &&
                !lower.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = trimmed.Length == prefix.Length
                ? ""
                : trimmed[prefix.Length..].TrimStart(':', '=', ' ');
            raw = raw
                .Replace("seconds", "", StringComparison.OrdinalIgnoreCase)
                .Replace("second", "", StringComparison.OrdinalIgnoreCase)
                .Replace("secs", "", StringComparison.OrdinalIgnoreCase)
                .Replace("sec", "", StringComparison.OrdinalIgnoreCase)
                .TrimEnd('s', 'S')
                .Trim();

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    void Normalize(CustomSequencesConfig config)
    {
        var normalized = new Dictionary<string, CustomSequenceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in config.Sequences)
        {
            var id = NormalizeId(string.IsNullOrWhiteSpace(pair.Value.Id) ? pair.Key : pair.Value.Id);
            pair.Value.Id = id;
            Normalize(pair.Value);
            if (!string.IsNullOrWhiteSpace(id))
                normalized[id] = pair.Value;
        }

        config.Sequences = normalized;
    }

    void Normalize(CustomSequenceDefinition sequence)
    {
        sequence.Id = NormalizeId(sequence.Id);
        if (string.IsNullOrWhiteSpace(sequence.DisplayName))
            sequence.DisplayName = sequence.Id;
        if (string.IsNullOrWhiteSpace(sequence.RiskLevel))
            sequence.RiskLevel = DetermineRisk(sequence);
        sequence.RequiresApproval = !sequence.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase);
        if (sequence.CreatedUtc == default)
            sequence.CreatedUtc = DateTime.UtcNow;
        if (sequence.UpdatedUtc == default)
            sequence.UpdatedUtc = sequence.CreatedUtc;

        foreach (var step in sequence.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Kind))
                step.Kind = string.IsNullOrWhiteSpace(step.Action) ? "note" : "action";
            step.Kind = step.Kind.Trim().ToLowerInvariant();
            step.Repeat = Math.Clamp(step.Repeat <= 0 ? 1 : step.Repeat, 1, MaxActionsPerSequence);
            step.DelaySeconds = Math.Max(0, step.DelaySeconds);
            step.IntervalSeconds = Math.Max(0, step.IntervalSeconds);
            if (step.Kind.Equals("action", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(step.Action))
                step.Action = ActionParameterConverter.NormalizeActionString(step.Action);
        }

        AssignStepIds(sequence.Steps);
        sequence.RiskLevel = DetermineRisk(sequence);
        sequence.RequiresApproval = !sequence.RiskLevel.Equals("safe", StringComparison.OrdinalIgnoreCase);
    }

    static void AssignStepIds(IReadOnlyList<CustomSequenceStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(steps[i].StepId))
                steps[i].StepId = $"step_{i + 1:000}";
        }
    }

    static string DetermineRisk(CustomSequenceDefinition sequence)
    {
        var risk = "safe";
        foreach (var step in sequence.Steps.Where(s => s.Enabled && s.Kind.Equals("action", StringComparison.OrdinalIgnoreCase)))
        {
            var name = FlowActionExecutor.ParseActionString(step.Action).actionName;
            if (name.Contains("destroy", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("clear", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("death", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("mode.end", StringComparison.OrdinalIgnoreCase))
                return "destructive";

            if (!name.Contains("notify", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("announce", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("query", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("count", StringComparison.OrdinalIgnoreCase))
                risk = "controlled";
        }

        return risk;
    }

    static bool IsCustomSequenceAction(string actionStringOrName)
    {
        var name = actionStringOrName.Contains(':')
            ? FlowActionExecutor.ParseActionString(actionStringOrName).actionName
            : actionStringOrName;

        return name.Equals("sequence.custom.play", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("sequence.custom.run", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("sequence.custom.execute", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("sequence.custom.preview", StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeId(string value)
    {
        var chars = (value ?? "").Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    static string First(Dictionary<string, string> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    static bool Bool(Dictionary<string, string> parameters, bool fallback, params string[] keys)
    {
        var value = First(parameters, keys);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CustomSequenceSummary
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ModeId { get; set; } = "";
    public int Actions { get; set; }
    public int Steps { get; set; }
    public bool HasTiming { get; set; }
    public string RiskLevel { get; set; } = "controlled";
}
