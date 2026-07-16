using System.Text;
using BattleLuck.Models;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// Read-only-by-default roadmap and prompt registry shared by server operators,
/// developer commands, and the LLM system prompt. Configuration changes still go
/// through the normal reviewed config workflow.
/// </summary>
public sealed class RoadmapService : IDisposable
{
    static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planned", "active", "blocked", "completed"
    };

    readonly object _sync = new();
    RoadmapDefinition _definition = new();
    DateTime _loadedAtUtc;
    string? _lastError;
    bool _disposed;

    public string ConfigPath => Path.Combine(ConfigLoader.ConfigRoot, "roadmap.json");
    public bool IsLoaded { get { lock (_sync) return _loadedAtUtc != default; } }
    public string? LastError { get { lock (_sync) return _lastError; } }

    public void Initialize() => Reload();

    public void Reload()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            try
            {
                ConfigLoader.EnsureDefaultsDeployed();
                if (!File.Exists(ConfigPath))
                {
                    _definition = new RoadmapDefinition();
                    _lastError = $"Missing roadmap config: {ConfigPath}";
                }
                else
                {
                    var loaded = JsonSerializer.Deserialize<RoadmapDefinition>(
                        File.ReadAllText(ConfigPath), ConfigLoader.JsonOptions);
                    _definition = loaded ?? new RoadmapDefinition();
                    Normalize(_definition);
                    _lastError = null;
                }

                _loadedAtUtc = DateTime.UtcNow;
                BattleLuckPlugin.LogInfo($"[Roadmap] Loaded {_definition.Milestones.Count} milestone(s) and {_definition.Roles.Count} role prompt(s).");
            }
            catch (Exception ex)
            {
                _definition = new RoadmapDefinition();
                _lastError = ex.Message;
                _loadedAtUtc = DateTime.UtcNow;
                BattleLuckPlugin.LogWarning($"[Roadmap] Failed to load {ConfigPath}: {ex}");
            }
        }
    }

    public RoadmapSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new RoadmapSnapshot
            {
                Project = _definition.Project,
                Description = _definition.Description,
                LoadedAtUtc = _loadedAtUtc,
                Milestones = _definition.Milestones.Select(Clone).ToArray(),
                Roles = _definition.Roles.Select(Clone).ToArray()
            };
        }
    }

    public RoadmapMilestone? FindMilestone(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        lock (_sync)
        {
            var milestone = _definition.Milestones.FirstOrDefault(m =>
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            return milestone == null ? null : Clone(milestone);
        }
    }

    public string BuildPromptContext(string roleId = "llm", string? focusId = null)
    {
        var snapshot = GetSnapshot();
        var sb = new StringBuilder();

        sb.AppendLine("SERVER ROADMAP (read-only context)");
        sb.AppendLine($"Project: {snapshot.Project}");
        sb.AppendLine($"Description: {snapshot.Description}");
        sb.AppendLine($"Loaded: {snapshot.LoadedAtUtc:O}");
        sb.AppendLine();

        var role = snapshot.Roles.FirstOrDefault(r => string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));
        if (role != null)
        {
            sb.AppendLine($"Role: {role.Title} ({role.Id})");
            sb.AppendLine($"Role scope: {role.Description}");
            if (role.Capabilities.Count > 0)
                sb.AppendLine($"Capabilities: {string.Join(", ", role.Capabilities)}");
            if (role.Guardrails.Count > 0)
                sb.AppendLine($"Guardrails: {string.Join("; ", role.Guardrails)}");
            sb.AppendLine();
        }

        foreach (var milestone in snapshot.Milestones)
        {
            var focus = !string.IsNullOrWhiteSpace(focusId) &&
                        string.Equals(milestone.Id, focusId, StringComparison.OrdinalIgnoreCase);
            sb.AppendLine($"- [{milestone.Status}] {milestone.Id}: {milestone.Title}{(focus ? " (FOCUS)" : "")}");
            sb.AppendLine($"  {milestone.Summary}");
            if (milestone.Acceptance.Count > 0)
                sb.AppendLine($"  Acceptance: {string.Join(" | ", milestone.Acceptance)}");
        }

        return sb.ToString().TrimEnd();
    }

    public string BuildSystemPrompt(string basePrompt, string roleId = "llm", string? focusId = null)
    {
        if (string.IsNullOrWhiteSpace(basePrompt))
            basePrompt = "You are the BattleLuck server assistant.";

        var rolePrompt = LoadRolePrompt(roleId);
        var context = BuildPromptContext(roleId, focusId);
        return $"{basePrompt.Trim()}\n\n{rolePrompt?.Trim() ?? string.Empty}\n\n{context}".Trim();
    }

    public string? LoadRolePrompt(string roleId)
    {
        RoadmapRole? role;
        lock (_sync)
        {
            role = _definition.Roles.FirstOrDefault(r =>
                string.Equals(r.Id, roleId, StringComparison.OrdinalIgnoreCase));
            role = role == null ? null : Clone(role);
        }

        if (role == null || string.IsNullOrWhiteSpace(role.PromptFile))
            return null;

        var path = Path.Combine(ConfigLoader.ConfigRoot, role.PromptFile.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Roadmap] Failed to read role prompt '{path}': {ex}");
            return null;
        }
    }

    public bool IsValidStatus(string status) => ValidStatuses.Contains(status ?? "");

    static void Normalize(RoadmapDefinition definition)
    {
        definition.Milestones ??= new();
        definition.Roles ??= new();
        foreach (var milestone in definition.Milestones)
        {
            milestone.Dependencies ??= new();
            milestone.Acceptance ??= new();
            milestone.PromptRefs ??= new();
            if (!ValidStatuses.Contains(milestone.Status))
                milestone.Status = "planned";
        }
        foreach (var role in definition.Roles)
        {
            role.Capabilities ??= new();
            role.Guardrails ??= new();
        }
    }

    static RoadmapMilestone Clone(RoadmapMilestone value) => new()
    {
        Id = value.Id,
        Title = value.Title,
        Status = value.Status,
        Summary = value.Summary,
        Owner = value.Owner,
        Dependencies = value.Dependencies.ToList(),
        Acceptance = value.Acceptance.ToList(),
        PromptRefs = value.PromptRefs.ToList()
    };

    static RoadmapRole Clone(RoadmapRole value) => new()
    {
        Id = value.Id,
        Title = value.Title,
        Description = value.Description,
        PromptFile = value.PromptFile,
        Capabilities = value.Capabilities.ToList(),
        Guardrails = value.Guardrails.ToList()
    };

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _definition = new RoadmapDefinition();
            _lastError = null;
        }
    }
}
