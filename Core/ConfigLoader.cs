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
    static string? _configRoot;
    static volatile bool _defaultsEnsured;
    static readonly object _defaultsEnsuredLock = new();

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
        }
    }

    /// <summary>
    /// Directory where optional BattleLuck helper tools are extracted on first load.
    /// Tools are kept below the config root so a server owner can inspect or remove
    /// them without touching the plugin binary.
    /// </summary>
    public static string ToolsRoot => Path.Combine(ConfigRoot, "tools");

    public static AIConfig LoadAIConfig()
    {
        if (_aiConfig != null)
            return _aiConfig;
        EnsureDefaultsDeployed();
        var aiConfigPath = Path.Combine(ConfigRoot, "ai_config.json");
        var config = LoadJson<AIConfig>(aiConfigPath) ?? new AIConfig();
        ApplyEnvOverrides(config);
        _aiConfig = config;
        return config;
    }

    static void ApplyEnvOverrides(AIConfig config)
    {
        if (config == null) return;
        var cloudflareToken = Env.Get("CLOUDFLARE_AI_API_TOKEN");
        if (IsUsableConfigValue(cloudflareToken))
            config.CloudflareAI.ApiToken = cloudflareToken!;
        else if (!string.IsNullOrWhiteSpace(cloudflareToken))
            BattleLuckPlugin.Log?.LogWarning("[ConfigLoader] Ignoring placeholder CLOUDFLARE_AI_API_TOKEN.");
        var cloudflareAccountId = Env.Get("CLOUDFLARE_AI_ACCOUNT_ID");
        if (IsUsableConfigValue(cloudflareAccountId))
            config.CloudflareAI.AccountId = cloudflareAccountId!;
        var cloudflareGatewayId = Env.Get("CLOUDFLARE_AI_GATEWAY_ID");
        if (IsUsableConfigValue(cloudflareGatewayId))
            config.CloudflareAI.GatewayId = cloudflareGatewayId!;
        var cloudflareModel = Env.Get("CLOUDFLARE_AI_MODEL");
        if (IsUsableConfigValue(cloudflareModel))
            config.CloudflareAI.Model = cloudflareModel!;
        var cloudflareTimeout = Env.Get("CLOUDFLARE_AI_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(cloudflareTimeout) && int.TryParse(cloudflareTimeout, out var timeout))
            config.CloudflareAI.TimeoutSeconds = timeout;
        var llamaKey = FirstEnv("LLAMA_API_KEY", "META_LLAMA_API_KEY");
        if (IsUsableConfigValue(llamaKey))
            config.LlamaAPI.ApiKey = llamaKey!;
        else if (!string.IsNullOrWhiteSpace(llamaKey))
            BattleLuckPlugin.Log?.LogWarning("[ConfigLoader] Ignoring placeholder Llama API key.");
        var llamaModel = FirstEnv("LLAMA_API_MODEL", "META_LLAMA_MODEL");
        if (IsUsableConfigValue(llamaModel))
            config.LlamaAPI.Model = llamaModel!;
        var llamaBaseUrl = FirstEnv("LLAMA_API_BASE_URL", "META_LLAMA_BASE_URL");
        if (IsUsableConfigValue(llamaBaseUrl))
            config.LlamaAPI.BaseUrl = llamaBaseUrl!;
        var llamaTimeout = Env.Get("LLAMA_API_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(llamaTimeout) && int.TryParse(llamaTimeout, out var llamaTimeoutSeconds))
            config.LlamaAPI.TimeoutSeconds = llamaTimeoutSeconds;
        var googleKey = FirstEnv("GOOGLE_AI_API_KEY", "GOOGLE_API_KEY", "GEMINI_API_KEY", "GOOGLE_AI_STUDIO_API_KEY");
        if (IsUsableConfigValue(googleKey))
            config.GoogleAIStudio.ApiKey = googleKey!;
        else if (!string.IsNullOrWhiteSpace(googleKey))
            BattleLuckPlugin.Log?.LogWarning("[ConfigLoader] Ignoring placeholder Google AI API key.");
        var googleModel = Env.Get("GOOGLE_AI_MODEL");
        if (IsUsableConfigValue(googleModel))
            config.GoogleAIStudio.Model = googleModel!;
        var sidecarUrl = Env.Get("BATTLE_AI_SIDECAR_URL");
        if (IsUsableConfigValue(sidecarUrl))
            config.Sidecar.BaseUrl = sidecarUrl!;
        var sidecarAuthKey = Env.Get("BATTLE_AI_SIDECAR_AUTH_KEY");
        if (IsUsableConfigValue(sidecarAuthKey))
            config.Sidecar.AuthKey = sidecarAuthKey!;
        var webhook = Env.Get("MESSAGING_DISCORD_WEBHOOK_URL");
        if (!string.IsNullOrWhiteSpace(webhook))
        {
            if (Uri.TryCreate(webhook, UriKind.Absolute, out _))
                config.Messaging.DiscordWebhookUrl = webhook;
            else
                BattleLuckPlugin.Log?.LogWarning($"[ConfigLoader] Ignoring invalid MESSAGING_DISCORD_WEBHOOK_URL: not a valid absolute URI.");
        }
    }

    public static DiscordBridgeConfig? LoadDiscordBridgeConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "discord_bridge.json");
        return LoadJson<DiscordBridgeConfig>(path);
    }

    public static WebhookConfig? LoadWebhookConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "webhook.json");
        return LoadJson<WebhookConfig>(path);
    }

    static string? FirstEnv(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Env.Get(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    static bool IsUsableConfigValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var upper = value.Trim().ToUpperInvariant();
        return !(upper.Contains("REPLACE-WITH") ||
                 upper.Contains("REPLACE_WITH") ||
                 upper.Contains("PLACEHOLDER") ||
                 upper.Contains("YOUR_") ||
                 upper.Contains("TOKEN_HERE") ||
                 upper.Contains("API_KEY_HERE") ||
                 upper.Contains("CHANGE_ME") ||
                 upper.Contains("CHANGEME") ||
                 upper.Contains("DUMMY") ||
                 upper.Contains("HMAC_SECRET"));
    }

    public static AiLoggerConfig? LoadAiLoggerConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "ai_logger.json");
        var config = LoadJson<AiLoggerConfig>(path);
        if (config != null)
            ApplyAiLoggerEnvOverrides(config);
        return config;
    }

    static void ApplyAiLoggerEnvOverrides(AiLoggerConfig config)
    {
        var googleKey = Env.Get("GOOGLE_AI_API_KEY");
        if (!string.IsNullOrWhiteSpace(googleKey))
            config.Providers.Gemini.ApiKey = googleKey;
        var sidecarKey = Env.Get("BATTLE_AI_SIDECAR_AUTH_KEY");
        if (!string.IsNullOrWhiteSpace(sidecarKey))
            config.Providers.SuperuserSidecar.ApiKey = sidecarKey;
        var sidecarUrl = Env.Get("BATTLE_AI_SIDECAR_URL");
        if (!string.IsNullOrWhiteSpace(sidecarUrl))
            config.Providers.SuperuserSidecar.Url = sidecarUrl;
        var discordWebhook = Env.Get("MESSAGING_DISCORD_WEBHOOK_URL");
        if (!string.IsNullOrWhiteSpace(discordWebhook))
            config.Discord.WebhookUrl = discordWebhook;
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
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Failed to load tech_catalog.json: {ex.Message}");
        }
        return new TechCatalog();
    }

    public static MCPRuntimeSettings? LoadMCPConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "mcp_config.json");
        return LoadJson<MCPRuntimeSettings>(path);
    }

    public static ActionConfig LoadActionConfig()
    {
        if (_actionConfig != null)
            return _actionConfig;
        EnsureDefaultsDeployed();
        var actionConfigPath = Path.Combine(ConfigRoot, "action_config.json");
        _actionConfig = LoadJson<ActionConfig>(actionConfigPath) ?? new ActionConfig();
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

        var config = new EventDefinitionLoader().LoadEffectiveConfig(modeId);
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
            var toolsDeployed = DeployEmbeddedFiles(
                assembly,
                $"{assemblyName}.tools.",
                ToolsRoot);
            var allowlistsDeployed = DeployEmbeddedFiles(
                assembly,
                $"{assemblyName}.docs.audit.systems.allowlists.",
                Path.Combine(ConfigRoot, "audit", "systems", "allowlists"));

            if (configDeployed > 0 || toolsDeployed > 0 || allowlistsDeployed > 0)
            {
                var details = new List<string>();
                if (configDeployed > 0)
                    details.Add($"{configDeployed} config file(s)");
                if (toolsDeployed > 0)
                    details.Add($"{toolsDeployed} tool file(s)");
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
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Failed to extract embedded defaults/tools: {ex.Message}");
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
                BattleLuckPlugin.LogWarning($"[ConfigLoader] Missing config: {path}");
            return null;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigLoader] JSON parse error in {Path.GetFileName(path)}: {ex.Message} (path: {path})");
            return null;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Error loading {path}: {ex.Message}");
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
    [JsonPropertyName("checkIntervalMs")]
    public int CheckIntervalMs { get; set; } = 500;
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
