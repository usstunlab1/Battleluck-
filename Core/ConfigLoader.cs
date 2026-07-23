using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BattleLuck.Models;
using BattleLuck.Services.Runtime;

/// <summary>
/// Loads and caches per-mode configuration from JSON files.
/// Config root: {BepInEx}/config/BattleLuck/{modeId}
///</summary>
public static class ConfigLoader
{
    static readonly Dictionary<string, ModeConfig> _cache = new();
    static AIConfig? _aiConfig;
    static ActionConfig? _actionConfig;
    static BattleLuckConfig? _ownerConfig;
    static string? _configRoot;
    static volatile bool _defaultsEnsured;
    static readonly object _defaultsEnsuredLock = new();
    internal static Action<string>? DiagnosticInfoSink { get; set; }
    internal static Action<string>? DiagnosticWarningSink { get; set; }

    public static JsonSerializerOptions JsonOptions => JsonOpts;

    public static string ConfigRoot
    {
        get
        {
            if (_configRoot == null)
            {
                _configRoot = Path.Combine(BepInEx.Paths.ConfigPath, "BattleLuck");
            }
            return _configRoot;
        }
        set
        {
            _configRoot = value;
            lock (_defaultsEnsuredLock)
            {
                _defaultsEnsured = false;  // Thread-safe flag reset
            }
            _aiConfig = null;
            _ownerConfig = null;
        }
    }

    public static AIConfig LoadAIConfig()
    {
        if (_aiConfig != null)
            return _aiConfig;
        EnsureDefaultsDeployed();
        var aiConfigPath = Path.Combine(ConfigRoot, "ai_config.json");
        var config = LoadJson<AIConfig>(aiConfigPath) ?? new AIConfig();
        ApplyEnvOverrides(config);
        PluginSettings.ApplyAIOverrides(config);
        EnforceServerOnlyAiProfile(config);
        _aiConfig = config;
        return config;
    }

    static void EnforceServerOnlyAiProfile(AIConfig config)
    {
        // The revamp supports exactly one optional local HTTP adapter. Legacy
        // cloud, sidecar, and MCP settings may still deserialize for one migration
        // release, but they cannot become executable runtime capabilities.
        config.Provider = "llama";
        config.LlamaAPI.Enabled = true;
        if (!Uri.TryCreate(config.LlamaAPI.BaseUrl, UriKind.Absolute, out var localEndpoint) ||
            localEndpoint.Scheme is not ("http" or "https") || !localEndpoint.IsLoopback)
        {
            config.LlamaAPI.BaseUrl = "http://127.0.0.1:11434";
        }
        config.LlamaAPI.Model = string.IsNullOrWhiteSpace(config.LlamaAPI.Model)
            ? "qwen2.5:0.5b"
            : config.LlamaAPI.Model;
        config.OpenAIAPI.Enabled = false;
        config.OpenAIAPI.ApiKey = "";
        config.GoogleAIStudio.ApiKey = "";
        config.GoogleAIStudio.FallbackModels.Clear();
        config.CloudflareAI.Enabled = false;
        config.CloudflareAI.AccountId = "";
        config.CloudflareAI.ApiToken = "";
        config.Sidecar.Enabled = false;
        config.Sidecar.AuthKey = "";
        config.McpRuntime.Enabled = false;
        config.McpRuntime.Servers.Clear();
    }

    public static BattleLuckConfig LoadBattleLuckConfig()
    {
        if (_ownerConfig != null)
            return _ownerConfig;
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "battleluck.json");
        var config = LoadJson<BattleLuckConfig>(path) ?? new BattleLuckConfig();
        config.Results.Keep = Math.Clamp(config.Results.Keep, 1, 1000);
        if (config.Schema != 1)
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Unsupported battleluck.json schema {config.Schema}; safe schema-1 defaults are used for new fields.");
        if (config.Chat.KillfeedScope is not ("off" or "event" or "global"))
            config.Chat.KillfeedScope = "event";
        if (!Uri.TryCreate(config.Assistant.LocalUrl, UriKind.Absolute, out var localUri) ||
            localUri.Scheme is not ("http" or "https"))
            config.Assistant.LocalUrl = "http://127.0.0.1:11434";
        _ownerConfig = config;
        EnsureRuntimeDirectories();
        return config;
    }

    public static bool TryReloadBattleLuckConfig(out string message)
    {
        var path = Path.Combine(ConfigRoot, "battleluck.json");
        try
        {
            var candidate = JsonSerializer.Deserialize<BattleLuckConfig>(File.ReadAllText(path), JsonOpts);
            if (candidate == null) { message = "battleluck.json is empty."; return false; }
            if (candidate.Schema != 1) { message = $"Unsupported schema {candidate.Schema}."; return false; }
            if (candidate.Results.Keep is < 1 or > 1000) { message = "results.keep must be between 1 and 1000."; return false; }
            if (candidate.Chat.KillfeedScope is not ("off" or "event" or "global"))
            { message = "chat.killfeed_scope must be off, event, or global."; return false; }
            if (!Uri.TryCreate(candidate.Assistant.LocalUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            { message = "assistant.local_url must be an absolute HTTP(S) URL."; return false; }
            _ownerConfig = candidate;
            EnsureRuntimeDirectories();
            message = "Configuration validated and reloaded.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Configuration rejected; the previous live configuration remains active: {ex.Message}";
            return false;
        }
    }

    static void EnsureRuntimeDirectories()
    {
        var root = Path.Combine(ConfigRoot, "runtime");
        Directory.CreateDirectory(Path.Combine(root, "ledger"));
        Directory.CreateDirectory(Path.Combine(root, "results"));
        Directory.CreateDirectory(Path.Combine(root, "recovery"));
        Directory.CreateDirectory(Path.Combine(ConfigRoot, "logs"));
    }

    static void ApplyEnvOverrides(AIConfig config)
    {
        var llamaModel = Env.Get("BATTLELUCK_LOCAL_LLM_MODEL");
        if (!string.IsNullOrWhiteSpace(llamaModel)) config.LlamaAPI.Model = llamaModel.Trim();
        var llamaBaseUrl = Env.Get("BATTLELUCK_LOCAL_LLM_URL");
        if (!string.IsNullOrWhiteSpace(llamaBaseUrl)) config.LlamaAPI.BaseUrl = llamaBaseUrl.Trim();
        var llamaTimeout = Env.Get("BATTLELUCK_LOCAL_LLM_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(llamaTimeout) && int.TryParse(llamaTimeout, out var llamaTimeoutSeconds))
            config.LlamaAPI.TimeoutSeconds = llamaTimeoutSeconds;

        var openAiApiKey = Env.Get("BATTLELUCK_OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiApiKey))
        {
            config.OpenAIAPI.ApiKey = openAiApiKey.Trim();
            config.OpenAIAPI.Enabled = true;
        }
        var openAiModel = Env.Get("BATTLELUCK_OPENAI_MODEL");
        if (!string.IsNullOrWhiteSpace(openAiModel)) config.OpenAIAPI.Model = openAiModel.Trim();
        var openAiBaseUrl = Env.Get("BATTLELUCK_OPENAI_BASE_URL");
        if (!string.IsNullOrWhiteSpace(openAiBaseUrl)) config.OpenAIAPI.BaseUrl = openAiBaseUrl.Trim();
        var openAiTimeout = Env.Get("BATTLELUCK_OPENAI_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(openAiTimeout) && int.TryParse(openAiTimeout, out var openAiTimeoutSeconds))
            config.OpenAIAPI.TimeoutSeconds = openAiTimeoutSeconds;
    }

    public static TechCatalog LoadTechCatalog()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "tech_catalog.json");
        if (!File.Exists(path))
        {
            BattleLuckPlugin.LogInfo($"[ConfigLoader] tech_catalog.json not found at {path}, returning empty catalog.");
            return new TechCatalog();
        }
        try
        {
            var json = File.ReadAllText(path);
            var catalogRoot = System.Text.Json.JsonSerializer.Deserialize<TechCatalogRoot>(json, JsonOpts);
            if (catalogRoot != null)
            {
                var runtimeCatalog = new TechCatalog();
                runtimeCatalog.Load(catalogRoot.Techs, catalogRoot.StackGroups);
                return runtimeCatalog;
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogInfo($"[ConfigLoader] Failed to load tech_catalog.json: {ex.Message}");
        }
        return new TechCatalog();
    }

    public static ActionConfig LoadActionConfig()
    {
        if (_actionConfig != null)
            return _actionConfig;
        EnsureDefaultsDeployed();
        var actionConfigPath = Path.Combine(ConfigRoot, "action_config.json");
        _actionConfig = LoadJson<ActionConfig>(actionConfigPath, optional: true) ?? new ActionConfig();
        return _actionConfig;
    }

    public static MerchantCommandConfig LoadMerchantCommandConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "merchant_servant_actions.json");
        return LoadJson<MerchantServantActionConfig>(path, optional: true)?.Merchants ?? new MerchantCommandConfig();
    }

    public static void ReloadAIConfig()
    {
        _aiConfig = null;
        LoadAIConfig();
    }

    public static void ReloadActionConfig()
    {
        _actionConfig = null;
        LoadActionConfig();
    }

    public static ModeConfig Load(string modeId)
    {
        EnsureDefaultsDeployed();
        if (_cache.TryGetValue(modeId, out var cached))
            return cached;

        var config = BattleLuck.Core.Loaders.ModeConfigLoader.Load(modeId);
        config.Border = null;
        _cache[modeId] = config;
        return config;
    }

    public static void Reload(string modeId)    
    {
        _cache.Remove(modeId);
        ConfigAdapter.Invalidate(modeId);
        Load(modeId);
    }

    public static void ReloadAll()
    {
        var ids = _cache.Keys.ToList();
        _cache.Clear();
        ConfigAdapter.InvalidateAll();
        foreach (var id in ids) Load(id);
    }

    public static void InvalidateCache()
    {
        _cache.Clear();
        ConfigAdapter.InvalidateAll();
        _aiConfig = null;
        _actionConfig = null;
        _ownerConfig = null;
        _defaultsEnsured = false;
    }

    public static void EnsureDefaultsDeployed()
    {
        lock (_defaultsEnsuredLock)
        {
            if (_defaultsEnsured)
                return;
            _defaultsEnsured = true;
        }

        try
        {
            Directory.CreateDirectory(ConfigRoot);
            var assembly = typeof(BattleLuckPlugin).Assembly;
            var assemblyName = assembly.GetName().Name ?? nameof(BattleLuckPlugin);
            var configDeployed = DeployEmbeddedFiles(
                assembly,
                $"{assemblyName}.config.BattleLuck.",
                ConfigRoot);
            UnifiedEventMigrationService.MigrateSplitDefinitions(ConfigRoot, BattleLuckPlugin.LogInfo,
                BattleLuckPlugin.LogWarning);
            var allowlistsDeployed = DeployEmbeddedFiles(
                assembly,
                $"{assemblyName}.docs.audit.systems.allowlists.",
                Path.Combine(ConfigRoot, "audit", "systems", "allowlists"));

            if (configDeployed > 0 || allowlistsDeployed > 0)
            {
                var details = new List<string>();
                if (configDeployed > 0)
                    details.Add($"{configDeployed} config file(s)");
                if (allowlistsDeployed > 0)
                    details.Add($"{allowlistsDeployed} KindredExtract allowlist file(s)");
                BattleLuckPlugin.LogInfo($"[ConfigLoader] Extracted {string.Join(" and ", details)} under {ConfigRoot}");
            }
        }
        catch (Exception ex)
        {
            // Allow a later reload/startup to retry if the config directory was
            // temporarily unavailable (for example while the server is stopping).
            lock (_defaultsEnsuredLock)
            {
                _defaultsEnsured = false;
            }
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Failed to extract embedded defaults: {ex.Message}");
        }
    }

    static int DeployEmbeddedFiles(Assembly assembly, string resourcePrefix, string destinationRoot)
    {
        var deployed = 0;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal))
                continue;

            var relativePath = ToRelativeResourcePath(resourceName, resourcePrefix);
            if (string.IsNullOrEmpty(relativePath))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            var rootPath = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                BattleLuckPlugin.LogWarning($"[ConfigLoader] Skipping embedded resource outside destination: {resourceName}");
                continue;
            }

            // Defaults are intentionally additive. Never overwrite a server owner's
            // config, generated event, prompt, or tool changes during an update.
            if (File.Exists(targetPath))
                continue;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                continue;
            using var file = File.Create(targetPath);
            stream.CopyTo(file);
            deployed++;
        }
        return deployed;
    }

    static T? LoadJson<T>(string path, bool optional = false) where T : class
    {
        if (!File.Exists(path))
        {
            if (!optional)
                DiagnosticInfoSink?.Invoke($"[ConfigLoader] Missing config: {path}");
            return null;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            DiagnosticWarningSink?.Invoke($"[ConfigLoader] JSON parse error in {Path.GetFileName(path)}: {ex.Message} (path: {path})");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticWarningSink?.Invoke($"[ConfigLoader] Error loading {path}: {ex.Message}");
            return null;
        }
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static string? ToRelativeResourcePath(string resourceName, string resourcePrefix)
    {
        var remainder = resourceName[resourcePrefix.Length..];
        var parts = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        var fileName = $"{parts[^2]}.{parts[^1]}";
        if (parts.Length == 2)
            return fileName;
        var relativeDir = Path.Combine(parts.Take(parts.Length - 2).ToArray());
        return Path.Combine(relativeDir, fileName);
    }
}

// ── Data models ─────────────────────────────────────────────────────────
// NOTE: ModeConfig lives in Models/ModeConfig.cs and SessionConfig (with
// SessionFlowConfig and SessionRules) lives in Models/SessionConfig.cs.
// They were extracted here during the Stage A scaffolding refactor.

// ── session.json (shared helper) ────────────────────────────────────────
public sealed class DelayConfig
{
    [JsonPropertyName("seconds")]
    public int Seconds { get; set; }
}

// ── zones.json ──────────────────────────────────────────────────────────
public sealed class ZonesConfig
{
    [JsonPropertyName("detection")]
    public DetectionConfig Detection { get; set; } = new();
    [JsonPropertyName("autoEnter")]
    public AutoEnterConfig AutoEnter { get; set; } = new();
    [JsonPropertyName("zones")]
    public List<ZoneDefinition> Zones { get; set; } = new();
}

public sealed class DetectionConfig
{
    /// <summary>
    /// Default interval value for demonstration.
    /// </summary>
    public const int CHECK_INTERVAL_DEFAULT = 500;

    [JsonPropertyName("checkIntervalMs")]
    public int CheckIntervalMs { get; set; } = CHECK_INTERVAL_DEFAULT;
    [JsonPropertyName("positionThreshold")]
    public float PositionThreshold { get; set; } = 1.0f;
}

public sealed class AutoEnterConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("enterOnConnect")]
    public bool EnterOnConnect { get; set; }
    [JsonPropertyName("enterOnSpawn")]
    public bool EnterOnSpawn { get; set; } = true;
    [JsonPropertyName("exitOnDisconnect")]
    public bool ExitOnDisconnect { get; set; } = true;
    [JsonPropertyName("tickIntervalMs")]
    public int TickIntervalMs { get; set; } = 250;
    [JsonPropertyName("spawnResolveTimeoutMs")]
    public int SpawnResolveTimeoutMs { get; set; } = 15000;
}

public sealed class ZoneDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("hash")]
    public int Hash { get; set; }
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;
    [JsonPropertyName("kitId")]
    public string KitId { get; set; } = "";
    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();
    [JsonPropertyName("teleportSpawn")]
    public Vec3Config TeleportSpawn { get; set; } = new();
    [JsonPropertyName("center")]
    public Vec3Config Center { get; set; } = new();
    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 60f;
    [JsonPropertyName("exitRadius")]
    public float ExitRadius { get; set; } = 65f;
    [JsonPropertyName("isSafe")]
    public bool IsSafe { get; set; }
    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();
    [JsonPropertyName("boundary")]
    public BoundaryConfig? Boundary { get; set; }
    [JsonPropertyName("waypoints")]
    public WaypointConfig? Waypoints { get; set; }
    [JsonPropertyName("glowBorder")]
    public GlowBorderConfig? GlowBorder { get; set; }
    [JsonPropertyName("movingPlatform")]
    public MovingPlatformConfig? MovingPlatform { get; set; }
    [JsonPropertyName("lootCrates")]
    public LootCrateConfig? LootCrates { get; set; }
    [JsonPropertyName("glow")]
    public GlowConfig? Glow { get; set; }
    [JsonPropertyName("bosses")]
    public BossesConfig? Bosses { get; set; }
    [JsonPropertyName("objectiveId")]
    public string? ObjectiveId { get; set; }
    [JsonPropertyName("objectivePoints")]
    public int ObjectivePoints { get; set; }
    [JsonPropertyName("aiRules")]
    public ZoneAiRules? AiRules { get; set; }
    [JsonPropertyName("schematic")]
    public ZoneSchematic? Schematic { get; set; }
}

public sealed class BoundaryConfig
{
    [JsonPropertyName("policy")]
    public string Policy { get; set; } = "none";
    [JsonPropertyName("dot")]
    public DotBoundaryConfig? Dot { get; set; }
    [JsonPropertyName("walls")]
    public WallBoundaryConfig? Walls { get; set; }
}

public sealed class DotBoundaryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("warningRadiusPercent")]
    public float WarningRadiusPercent { get; set; } = 0.80f;
    [JsonPropertyName("dangerRadiusPercent")]
    public float DangerRadiusPercent { get; set; } = 0.95f;
    [JsonPropertyName("teleportOnExit")]
    public bool TeleportOnExit { get; set; } = true;
}

public sealed class WallBoundaryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("height")]
    public float Height { get; set; } = 15f;
    [JsonPropertyName("spacing")]
    public float Spacing { get; set; } = 5f;
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;
    [JsonPropertyName("wallPrefab")]
    public string? WallPrefab { get; set; }
    [JsonPropertyName("floorPrefab")]
    public string? FloorPrefab { get; set; }
    [JsonPropertyName("spawnWalls")]
    public bool SpawnWalls { get; set; } = true;
    [JsonPropertyName("spawnFloors")]
    public bool SpawnFloors { get; set; } = false;
    [JsonPropertyName("requireOnlineAdmin")]
    public bool RequireOnlineAdmin { get; set; } = true;
    [JsonPropertyName("floorSpacing")]
    public float? FloorSpacing { get; set; } = 2.5f;
    [JsonPropertyName("buffs")]
    public List<BorderBuffEntry> Buffs { get; set; } = new();
    [JsonPropertyName("timers")]
    public List<BorderTimerEntry> Timers { get; set; } = new();
    [JsonPropertyName("glow")]
    public GlowBorderConfig? Glow { get; set; }
}

public sealed class BorderBuffEntry
{
    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";
    [JsonPropertyName("duration")]
    public float Duration { get; set; } = -1f;
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

public sealed class BorderTimerEntry
{
    [JsonPropertyName("timerId")]
    public string TimerId { get; set; } = "";
    [JsonPropertyName("duration")]
    public float Duration { get; set; } = 60f;
    [JsonPropertyName("onComplete")]
    public string? OnComplete { get; set; }
    [JsonPropertyName("repeat")]
    public bool Repeat { get; set; } = false;
}

public sealed class Vec3Config
{
    [JsonPropertyName("x")]
    public float X { get; set; }
    [JsonPropertyName("y")]
    public float Y { get; set; }
    [JsonPropertyName("z")]
    public float Z { get; set; }
    public Unity.Mathematics.float3 ToFloat3() => new(X, Y, Z);
    public static Vec3Config FromFloat3(Unity.Mathematics.float3 value) => new()
    {
        X = value.x,
        Y = value.y,
        Z = value.z
    };
}

public sealed class FlowConfig
{
    [JsonPropertyName("delayBefore")]
    public DelayConfig DelayBefore { get; set; } = new();
    [JsonPropertyName("delayBetweenFlows")]
    public DelayConfig DelayBetweenFlows { get; set; } = new();
    [JsonPropertyName("delayBetweenActions")]
    public DelayConfig DelayBetweenActions { get; set; } = new();
    [JsonPropertyName("executionOrder")]
    public List<string> ExecutionOrder { get; set; } = new();
    [JsonPropertyName("flows")]
    public Dictionary<string, FlowDefinition> Flows { get; set; } = new();
}

public sealed class FlowDefinition
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
    [JsonPropertyName("delayBetweenActions")]
    public DelayConfig DelayBetweenActions { get; set; } = new();
    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();
}

// ── action_config.json ───────────────────────────────────────────────────
public sealed class ActionConfig
{
    [JsonPropertyName("actionTypes")]
    public List<ActionTypeConfig> ActionTypes { get; set; } = new();
    [JsonPropertyName("sequences")]
    public Dictionary<string, Dictionary<string, int>> Sequences { get; set; } = new();
    [JsonPropertyName("actionVFXMapping")]
    public Dictionary<string, string> ActionVFXMapping { get; set; } = new();
    [JsonPropertyName("actions")]
    public Dictionary<string, ActionInfoConfig> Actions { get; set; } = new();
    [JsonPropertyName("modeActions")]
    public Dictionary<string, List<string>> ModeActions { get; set; } = new();
}

public sealed class ActionTypeConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
    [JsonPropertyName("modes")]
    public List<string> Modes { get; set; } = new();
}

public sealed class ActionInfoConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
    [JsonPropertyName("coloredLabel")]
    public string ColoredLabel { get; set; } = "";
    [JsonPropertyName("defaultPoints")]
    public int DefaultPoints { get; set; }
}
