using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BattleLuck.Services.Flow;

namespace BattleLuck.Services.Runtime;

/// <summary>
/// A live, persisted catalog of verified ProjectM and Unity ECS system
/// references. A registration does not create or patch a native ECS system;
/// it makes a verified system reference available to BattleLuck's live action
/// and AI surfaces without requiring a plugin restart.
/// </summary>
public sealed class LiveSystemRegistration
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("systemType")]
    public string SystemType { get; set; } = "";

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("registeredBy")]
    public string RegisteredBy { get; set; } = "";

    [JsonPropertyName("registeredAtUtc")]
    public DateTime RegisteredAtUtc { get; set; }
}

sealed class LiveSystemRegistryFile
{
    [JsonPropertyName("systems")]
    public List<LiveSystemRegistration> Systems { get; set; } = new();
}

public static class LiveSystemRegistryService
{
    const string FileName = "live_system_registry.json";
    const int MaxDescriptionLength = 512;
    static readonly object Gate = new();
    static readonly Regex ActionNamePattern = new(
        "^[A-Za-z][A-Za-z0-9._-]{0,127}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly HashSet<string> ReservedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "system.find",
        "system.search",
        "system.register"
    };

    static string? _loadedPath;
    static DateTime _loadedWriteTimeUtc;
    static LiveSystemRegistryFile _state = new();

    public static string RegistryPath => Path.Combine(ConfigLoader.ConfigRoot, FileName);

    public static IReadOnlyList<LiveSystemRegistration> GetAll()
    {
        lock (Gate)
        {
            if (!TryLoadNoLock(out var error))
            {
                BattleLuckPlugin.LogWarning($"[LiveSystemRegistry] {error}");
                return Array.Empty<LiveSystemRegistration>();
            }

            return _state.Systems
                .OrderBy(entry => entry.Action, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
    }

    public static bool IsRegisteredAction(string actionName) =>
        TryGet(actionName, out _);

    public static bool TryGet(string actionName, out LiveSystemRegistration registration)
    {
        registration = default!;
        if (string.IsNullOrWhiteSpace(actionName))
            return false;

        lock (Gate)
        {
            if (!TryLoadNoLock(out var error))
            {
                BattleLuckPlugin.LogWarning($"[LiveSystemRegistry] {error}");
                return false;
            }

            var found = _state.Systems.FirstOrDefault(entry =>
                entry.Action.Equals(actionName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found == null)
                return false;

            registration = Clone(found);
            return true;
        }
    }

    public static OperationResult<LiveSystemRegistration> Register(
        string systemType,
        string runtime,
        string actionAlias = "",
        string description = "",
        string registeredBy = "")
    {
        systemType = systemType?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(systemType))
            return OperationResult<LiveSystemRegistration>.Fail("system.register requires 'systemType'.");

        if (!TryNormalizeRuntime(runtime, systemType, out var normalizedRuntime, out var runtimeError))
            return OperationResult<LiveSystemRegistration>.Fail(runtimeError);

        var reference = new KindredSystemReferenceService();
        if (!reference.TryGetSystem(systemType, out _))
        {
            return OperationResult<LiveSystemRegistration>.Fail(
                $"'{systemType}' is not an exact ProjectM/Unity system in the KindredExtract reference. Use system.search first; invented or unverified types cannot be registered.");
        }

        var action = NormalizeActionName(actionAlias, systemType);
        if (string.IsNullOrWhiteSpace(action))
        {
            return OperationResult<LiveSystemRegistration>.Fail(
                "system.register alias must be a valid action name such as system.projectm.ability_input.");
        }

        if (ReservedActions.Contains(action))
            return OperationResult<LiveSystemRegistration>.Fail($"'{action}' is reserved and cannot be replaced.");

        if (FlowActionExecutor.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase) ||
            FlowActionExecutor.Registry.TryGetAction(action, out _))
        {
            return OperationResult<LiveSystemRegistration>.Fail(
                $"Action '{action}' already belongs to BattleLuck's catalog or runtime registry. Choose a distinct system alias.");
        }

        description = (description ?? "").Trim();
        if (description.Length > MaxDescriptionLength)
            return OperationResult<LiveSystemRegistration>.Fail($"description may not exceed {MaxDescriptionLength} characters.");

        lock (Gate)
        {
            if (!TryLoadNoLock(out var loadError))
                return OperationResult<LiveSystemRegistration>.Fail(loadError);

            var existing = _state.Systems.FirstOrDefault(entry =>
                entry.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !existing.SystemType.Equals(systemType, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<LiveSystemRegistration>.Fail(
                    $"Action '{action}' is already registered for '{existing.SystemType}'. Choose another alias.");
            }

            var registration = existing ?? new LiveSystemRegistration();
            registration.Action = action;
            registration.SystemType = systemType;
            registration.Runtime = normalizedRuntime;
            registration.Description = description;
            registration.RegisteredBy = registeredBy?.Trim() ?? "";
            registration.RegisteredAtUtc = DateTime.UtcNow;

            if (existing == null)
                _state.Systems.Add(registration);

            var save = SaveNoLock();
            if (!save.Success)
                return OperationResult<LiveSystemRegistration>.Fail(save.Error ?? "Could not save live system registry.");

            BattleLuckPlugin.LogInfo(
                $"[LiveSystemRegistry] Registered {normalizedRuntime} system '{systemType}' as '{action}'.");
            return OperationResult<LiveSystemRegistration>.Ok(Clone(registration));
        }
    }

    static bool TryNormalizeRuntime(string runtime, string systemType, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        var requested = (runtime ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requested))
        {
            requested = systemType.StartsWith("ProjectM.", StringComparison.OrdinalIgnoreCase)
                ? "ProjectM"
                : systemType.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase)
                    ? "Unity"
                    : "";
        }

        if (requested.Equals("ProjectM", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "ProjectM";
            if (!systemType.StartsWith("ProjectM.", StringComparison.OrdinalIgnoreCase))
            {
                error = "runtime=ProjectM requires a systemType beginning with 'ProjectM.'.";
                return false;
            }
            return true;
        }

        if (requested.Equals("Unity", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Unity";
            if (!systemType.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase))
            {
                error = "runtime=Unity requires a systemType beginning with 'Unity.'.";
                return false;
            }
            return true;
        }

        error = "runtime must be ProjectM or Unity.";
        return false;
    }

    static string NormalizeActionName(string alias, string systemType)
    {
        var value = (alias ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            var suffix = Regex.Replace(systemType.ToLowerInvariant(), "[^a-z0-9]+", ".").Trim('.');
            value = $"system.{suffix}";
        }
        else if (!value.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            value = $"system.{value}";
        }

        return ActionNamePattern.IsMatch(value) ? value : "";
    }

    static bool TryLoadNoLock(out string error)
    {
        error = "";
        var path = RegistryPath;
        var writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_loadedPath != null &&
            _loadedPath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            _loadedWriteTimeUtc == writeTime)
        {
            return true;
        }

        try
        {
            if (!File.Exists(path))
            {
                _state = new LiveSystemRegistryFile();
            }
            else
            {
                _state = JsonSerializer.Deserialize<LiveSystemRegistryFile>(File.ReadAllText(path), ConfigLoader.JsonOptions)
                    ?? new LiveSystemRegistryFile();
            }

            NormalizeState(_state);
            _loadedPath = path;
            _loadedWriteTimeUtc = writeTime;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load {FileName}: {ex.Message}";
            return false;
        }
    }

    static OperationResult SaveNoLock()
    {
        try
        {
            Directory.CreateDirectory(ConfigLoader.ConfigRoot);
            NormalizeState(_state);
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions(ConfigLoader.JsonOptions)
            {
                WriteIndented = true
            });
            var path = RegistryPath;
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            _loadedPath = path;
            _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to save {FileName}: {ex.Message}");
        }
    }

    static void NormalizeState(LiveSystemRegistryFile state)
    {
        state.Systems ??= new List<LiveSystemRegistration>();
        state.Systems = state.Systems
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Action) && !string.IsNullOrWhiteSpace(entry.SystemType))
            .GroupBy(entry => entry.Action, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.RegisteredAtUtc).First())
            .ToList();
    }

    static LiveSystemRegistration Clone(LiveSystemRegistration source) => new()
    {
        Action = source.Action,
        SystemType = source.SystemType,
        Runtime = source.Runtime,
        Description = source.Description,
        RegisteredBy = source.RegisteredBy,
        RegisteredAtUtc = source.RegisteredAtUtc
    };
}
