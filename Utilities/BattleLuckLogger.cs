/// <summary>
/// Centralized logger wrapping BattleLuckPlugin.Log with severity levels and
/// structured context. When a Discord webhook URL is configured, all log entries
/// are also forwarded to Discord as fire-and-forget embeds.
///
/// Cloudflare-inspired UX principles applied:
/// - Help, not bureaucracy: Error reports include actionable context
/// - Attention, not alarm: Color-coded severity without overwhelming red
/// - Scannable, not verbose: Title + structured context fields
/// - Unified information architecture: Consistent embed format across all levels
/// </summary>
public static class BattleLuckLogger
{
    static string? _webhookUrl;
    static readonly System.Net.Http.HttpClient _http = new();
    public static bool IsDiscordForwardingEnabled => _webhookUrl != null;

    // Severity → Discord embed colour (decimal)
    // Red is reserved only for Critical; Warning uses amber, Error uses softer red.
    // Cloudflare principle: "Red at full saturation only for icons, never for backgrounds."
    static readonly System.Collections.Generic.Dictionary<string, int> _colours = new()
    {
        ["INFO"]     = 0x5865F2,   // blurple
        ["WARNING"]  = 0xFAA61A,   // amber
        ["ERROR"]    = 0xED4245,   // red (muted, not full saturation)
        ["CRITICAL"] = 0xE00000,   // bright red
        ["DEBUG"]    = 0x99AAB5,   // grey
    };

    /// <summary>Set once after config loads. Pass null to disable Discord forwarding.</summary>
    public static void SetDiscordWebhook(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !System.Uri.TryCreate(url.Trim(), System.UriKind.Absolute, out var uri) ||
            (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps) ||
            !uri.Scheme.Equals(System.Uri.UriSchemeHttps, System.StringComparison.OrdinalIgnoreCase) ||
            !(uri.Host.Equals("discord.com", System.StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("discordapp.com", System.StringComparison.OrdinalIgnoreCase)) ||
            !uri.AbsolutePath.StartsWith("/api/webhooks/", System.StringComparison.Ordinal))
        {
            _webhookUrl = null;
            return;
        }

        _webhookUrl = uri.AbsoluteUri;
    }

    public static void Info(string message)     { BattleLuckPlugin.Log?.LogInfo(message);                    Post("INFO",     message); }
    public static void Warning(string message)  { BattleLuckPlugin.Log?.LogWarning(message);                 Post("WARNING",  message); }
    public static void Error(string message)    { BattleLuckPlugin.Log?.LogError(message);                   Post("ERROR",    message); }
    public static void Log(string message)      { BattleLuckPlugin.Log?.LogInfo(message);                    Post("INFO",     message); }
    public static void Critical(string message) { BattleLuckPlugin.Log?.LogError($"[CRITICAL] {message}");  Post("CRITICAL", message); }

    public static void Debug(string message)
    {
#if DEBUG
        BattleLuckPlugin.Log?.LogInfo($"[DEBUG] {message}");
        Post("DEBUG", message);
#endif
    }

    /// <summary>
    /// Log an error with a structured context object that will be rendered as
    /// Discord embed fields alongside the main message.
    /// Follows Cloudflare's "scannable state name + action" pattern.
    /// </summary>
    /// <param name="message">The primary error description.</param>
    /// <param name="context">A labelled object describing the failure context (e.g. player, zone, action).</param>
    public static void ErrorWithContext(string message, object? context)
    {
        BattleLuckPlugin.Log?.LogError(message);
        PostWithContext("ERROR", message, context);
    }

    /// <summary>
    /// Log a warning with structured context.
    /// </summary>
    public static void WarningWithContext(string message, object? context)
    {
        BattleLuckPlugin.Log?.LogWarning(message);
        PostWithContext("WARNING", message, context);
    }

    /// <summary>
    /// Log a player-facing action result. Server-side logs the operation result,
    /// and the Discord embed includes what was communicated to the player.
    /// </summary>
    public static void UserAction(string playerName, string action, string result)
    {
        var message = $"[UserAction] {playerName} → {action}: {result}";
        BattleLuckPlugin.Log?.LogInfo(message);
        PostWithContext("INFO", $"User action: {action}", new { Player = playerName, Result = result });
    }

    // Called by BattleLuckPlugin.LogInfo/Warning/Error to forward without double-logging
    public static void Post_Internal(string level, string message) => Post(level, message);

    public static void Forward(string level, string message) => Post(level, message);

    // ── Discord forwarding ────────────────────────────────────────────────

    static void Post(string level, string message)
    {
        // Capture the webhook URL on the calling thread to prevent a null-URI
        // race when SetDiscordWebhook(null) runs concurrently on another thread.
        var url = _webhookUrl;
        if (url == null) return;
        _ = System.Threading.Tasks.Task.Run(() => PostAsync(url, level, message, null));
    }

    static void PostWithContext(string level, string message, object? context)
    {
        var url = _webhookUrl;
        if (url == null) return;
        _ = System.Threading.Tasks.Task.Run(() => PostAsync(url, level, message, context));
    }

    static async System.Threading.Tasks.Task PostAsync(string webhookUrl, string level, string message, object? context)
    {
        try
        {
            var colour = _colours.TryGetValue(level, out var c) ? c : 0x5865F2;
            var truncated = message.Length > 1024 ? message[..1021] + "..." : message;

            // Build embed fields
            var fields = new System.Collections.Generic.List<object>();
            if (context != null)
            {
                foreach (var prop in context.GetType().GetProperties())
                {
                    var val = prop.GetValue(context)?.ToString() ?? "(null)";
                    // Truncate long field values
                    var display = val.Length > 256 ? val[..253] + "..." : val;
                    fields.Add(new
                    {
                        name  = prop.Name,
                        value = $"`{display}`",
                        inline = true
                    });
                }
            }

            var embed = new System.Collections.Generic.Dictionary<string, object>
            {
                ["title"]       = $"[BattleLuck] {level}",
                ["description"] = truncated,
                ["color"]       = colour,
                ["timestamp"]   = System.DateTime.UtcNow.ToString("o")
            };

            if (fields.Count > 0)
                embed["fields"] = fields;

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                embeds = new[] { embed }
            });

            using var content = new System.Net.Http.StringContent(
                payload, System.Text.Encoding.UTF8, "application/json");
            await _http.PostAsync(webhookUrl, content).ConfigureAwait(false);
        }
        catch
        {
            // Never let Discord forwarding crash the server
        }
    }
}
