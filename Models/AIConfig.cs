using System.Text.Json.Serialization;

namespace BattleLuck.Models
{
    public class AIConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "llama";

        [JsonPropertyName("direct_chat_max_tokens")]
        public int DirectChatMaxTokens { get; set; } = 256;

        [JsonPropertyName("direct_chat_skip_sidecar_under_chars")]
        public int DirectChatSkipSidecarUnderChars { get; set; } = 180;

        [JsonPropertyName("google_ai")]
        public GoogleAIStudioSettings GoogleAIStudio { get; set; } = new();

        [JsonPropertyName("llama_api")]
        public LlamaAPISettings LlamaAPI { get; set; } = new();

        [JsonPropertyName("cloudflare_ai")]
        public CloudflareAISettings CloudflareAI { get; set; } = new();

        [JsonPropertyName("ai_sidecar")]
        public BattleAiSidecarSettings Sidecar { get; set; } = new();

        [JsonPropertyName("mcp_runtime")]
        public MCPRuntimeSettings McpRuntime { get; set; } = new();

        [JsonPropertyName("messaging")]
        public MessagingSettings Messaging { get; set; } = new();

        [JsonPropertyName("privacy")]
        public PrivacySettings Privacy { get; set; } = new();

        [JsonPropertyName("chat_backup")]
        public ChatBackupSettings ChatBackup { get; set; } = new();

        [JsonPropertyName("event_authoring")]
        public EventAuthoringSettings EventAuthoring { get; set; } = new();

        [JsonPropertyName("projectm_aigroup")]
        public ProjectMAiGroupSettings ProjectMAiGroup { get; set; } = new();
    }

    public class GoogleAIStudioSettings
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("max_requests_per_second")]
        public int MaxRequestsPerSecond { get; set; } = 10;

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.8f;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 300;

        [JsonPropertyName("fallback_models")]
        public List<string> FallbackModels { get; set; } = new();
    }

    public class LlamaAPISettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("base_url")]
        public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "llama2";

        [JsonPropertyName("max_requests_per_second")]
        public int MaxRequestsPerSecond { get; set; } = 10;

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.7f;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 256;

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 90;
    }

    public class MessagingSettings
    {
        [JsonPropertyName("message_cooldown_seconds")]
        public int MessageCooldownSeconds { get; set; } = 30;

        [JsonPropertyName("tip_cooldown_minutes")]
        public int TipCooldownMinutes { get; set; } = 5;

        [JsonPropertyName("context_retention_minutes")]
        public int ContextRetentionMinutes { get; set; } = 30;

        [JsonPropertyName("auto_tips_enabled")]
        public bool AutoTipsEnabled { get; set; } = true;

        [JsonPropertyName("welcome_messages_enabled")]
        public bool WelcomeMessagesEnabled { get; set; } = true;

        [JsonPropertyName("match_summaries_enabled")]
        public bool MatchSummariesEnabled { get; set; } = true;

        [JsonPropertyName("discord_webhook_url")]
        public string DiscordWebhookUrl { get; set; } = "";

        [JsonPropertyName("show_holograms")]
        public bool ShowHolograms { get; set; } = false;

        [JsonPropertyName("ai_colors")]
        public AIColorSettings AiColors { get; set; } = new();
    }

    public class AIColorSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "#66E3FF";

        [JsonPropertyName("default")]
        public string Default { get; set; } = "#D7E3FF";

        [JsonPropertyName("good")]
        public string Good { get; set; } = "#47FF8A";

        [JsonPropertyName("info")]
        public string Info { get; set; } = "#5CC8FF";

        [JsonPropertyName("warning")]
        public string Warning { get; set; } = "#FFD166";

        [JsonPropertyName("error")]
        public string Error { get; set; } = "#FF5C7A";

        [JsonPropertyName("admin")]
        public string Admin { get; set; } = "#C77DFF";

        [JsonPropertyName("event")]
        public string Event { get; set; } = "#FFB347";
    }

    public class PrivacySettings
    {
        [JsonPropertyName("opt_out_by_default")]
        public bool OptOutByDefault { get; set; } = false;

        [JsonPropertyName("allow_player_toggle")]
        public bool AllowPlayerToggle { get; set; } = true;

        [JsonPropertyName("store_conversation_history")]
        public bool StoreConversationHistory { get; set; } = false;

        [JsonPropertyName("max_conversation_history_size")]
        public int MaxConversationHistorySize { get; set; } = 0;
    }

    public class ChatBackupSettings
    {
        /// <summary>Disabled by default; the server owner must opt in to persistence.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Optional absolute path. When empty on Windows, the V Rising LocalLow
        /// directory is used automatically.
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("retention_days")]
        public int RetentionDays { get; set; } = 30;

        [JsonPropertyName("max_file_size_mb")]
        public int MaxFileSizeMb { get; set; } = 8;
    }

    public class EventAuthoringSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("max_actions_per_event")]
        public int MaxActionsPerEvent { get; set; } = 1000;

        [JsonPropertyName("config_edit_max_tokens")]
        public int ConfigEditMaxTokens { get; set; } = 32000;

        [JsonPropertyName("use_actions_catalog")]
        public bool UseActionsCatalog { get; set; } = true;

        [JsonPropertyName("pending_operation_minutes")]
        public int PendingOperationMinutes { get; set; } = 30;
    }

    public class ProjectMAiGroupSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("auto_execute")]
        public bool AutoExecute { get; set; } = false;

        [JsonPropertyName("snapshot_interval_seconds")]
        public int SnapshotIntervalSeconds { get; set; } = 15;

        [JsonPropertyName("max_units")]
        public int MaxUnits { get; set; } = 10;

        [JsonPropertyName("max_players")]
        public int MaxPlayers { get; set; } = 8;

        [JsonPropertyName("min_confidence")]
        public float MinConfidence { get; set; } = 0.65f;

        [JsonPropertyName("allowed_actions")]
        public List<string> AllowedActions { get; set; } = new()
        {
            "announce",
            "notification",
            "npc.aggro",
            "npc.follow",
            "npc.hold",
            "npc.release",
            "ai.boss.aggro",
            "ai.boss.deaggro",
            "ai.set_behavior",
            "boss.goto",
            "boss.goto.pos",
            "boss.return_home"
        };

        [JsonPropertyName("action_policies")]
        public Dictionary<string, ProjectMAiGroupActionPolicy> ActionPolicies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["announce"] = new() { Enabled = true, AutoExecute = true, MinConfidence = 0.55f, CooldownSeconds = 30 },
            ["notification"] = new() { Enabled = true, AutoExecute = true, MinConfidence = 0.55f, CooldownSeconds = 30 },
            ["npc.aggro"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.75f, CooldownSeconds = 20, RequireActiveSession = true },
            ["npc.follow"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.75f, CooldownSeconds = 20, RequireActiveSession = true },
            ["npc.hold"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.7f, CooldownSeconds = 20, RequireActiveSession = true },
            ["ai.boss.aggro"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.8f, CooldownSeconds = 30, RequireActiveSession = true },
            ["ai.boss.deaggro"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.8f, CooldownSeconds = 30, RequireActiveSession = true },
            ["ai.set_behavior"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.8f, CooldownSeconds = 30, RequireActiveSession = true },
            ["boss.goto"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.8f, CooldownSeconds = 30, RequireActiveSession = true },
            ["boss.goto.pos"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.8f, CooldownSeconds = 30, RequireActiveSession = true },
            ["boss.return_home"] = new() { Enabled = true, AutoExecute = false, MinConfidence = 0.75f, CooldownSeconds = 30, RequireActiveSession = true }
        };
    }

    public class ProjectMAiGroupActionPolicy
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("auto_execute")]
        public bool AutoExecute { get; set; } = false;

        [JsonPropertyName("min_confidence")]
        public float MinConfidence { get; set; } = 0.65f;

        [JsonPropertyName("cooldown_seconds")]
        public int CooldownSeconds { get; set; } = 30;

        [JsonPropertyName("require_active_session")]
        public bool RequireActiveSession { get; set; } = true;

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = "";
    }

    public class MCPRuntimeSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "stdio";

        [JsonPropertyName("servers")]
        public Dictionary<string, MCPServerConfig> Servers { get; set; } = new();

        [JsonPropertyName("tools_cache_enabled")]
        public bool ToolsCacheEnabled { get; set; } = true;

        [JsonPropertyName("logs_enabled")]
        public bool LogsEnabled { get; set; } = false;
    }

    public class MCPServerConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Transport type: "stdio" for local process, "http" or "sse" for remote HTTP/MCP server.</summary>
        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "stdio";

        /// <summary>Remote MCP server URL (used when transport is "http" or "sse").</summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>Human-readable label for this server (optional, used in logs and status).</summary>
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("args")]
        public List<string> Args { get; set; } = new();

        [JsonPropertyName("env")]
        public Dictionary<string, string> Environment { get; set; } = new();

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class CloudflareAISettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = "";

        [JsonPropertyName("gateway_id")]
        public string? GatewayId { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("max_requests_per_second")]
        public int MaxRequestsPerSecond { get; set; } = 10;

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.8f;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 300;

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 90;
    }

}
