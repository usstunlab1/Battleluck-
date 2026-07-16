using System.Collections.Generic;

namespace BattleLuck.Models
{
    public class BattleAiSidecarSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("base_url")]
        public string BaseUrl { get; set; } = "http://127.0.0.1:3001";

        [JsonPropertyName("auth_key")]
        public string AuthKey { get; set; } = "";

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 10;
    }

    public class BattleAiPlayerContextDto
    {
        public string SteamId { get; set; } = "";
        public List<string> RecentEvents { get; set; } = new();
        public string ConversationSummary { get; set; } = "";
        public string LastActivityUtc { get; set; } = "";
    }

    public class BattleAiSessionPlayerDto
    {
        public string SteamId { get; set; } = "";
        public int Score { get; set; }
        public int? TeamId { get; set; }
        public bool IsRequester { get; set; }
    }

    public class BattleAiSessionContextDto
    {
        public string SessionId { get; set; } = "";
        public string ModeId { get; set; } = "";
        public int ZoneHash { get; set; }
        public double ElapsedSeconds { get; set; }
        public int TimeLimitSeconds { get; set; }
        public bool IsTimeUp { get; set; }
        public List<BattleAiSessionPlayerDto> Players { get; set; } = new();
        public List<BattleAiSessionPlayerDto> Leaderboard { get; set; } = new();
        public Dictionary<string, int> TeamScores { get; set; } = new();
    }

    public class BattleAiQueryEnrichmentRequest
    {
        public string Query { get; set; } = "";
        public BattleAiPlayerContextDto Player { get; set; } = new();
        public BattleAiSessionContextDto? Session { get; set; }
    }

    public class BattleAiQueryEnrichmentResult
    {
        public string Summary { get; set; } = "";
        public List<string> TacticalFocus { get; set; } = new();
        public List<string> AnswerHints { get; set; } = new();
        public string Confidence { get; set; } = "low";
        public string DetectedIntent { get; set; } = "general";
        public bool ShouldEscalateToModel { get; set; } = true;
    }

    public class BattleAiHealthResponse
    {
        public string Status { get; set; } = "unknown";
        public string Service { get; set; } = "";
        public string Version { get; set; } = "";
        public List<string> Features { get; set; } = new();
        public DateTime? TimestampUtc { get; set; }
    }
}