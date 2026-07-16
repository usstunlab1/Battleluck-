using ProjectM.Network;
using Unity.Collections;

/// <summary>
/// Enhanced notification utilities with structured messaging inspired by Cloudflare's
/// redesign principles: scannable state names, actionable troubleshooting, consistent
/// information architecture, and appropriate severity cues.
///
/// Design principles applied (sourced from Cloudflare Turnstile/Challenge Pages redesign):
/// - Help, not bureaucracy: Replace opaque errors with actionable messages
/// - Attention, not alarm: Reserved color/icon severity; neutral tone for recoverable issues
/// - Scannable, not verbose: Primary state name first, supporting detail accessible on demand
/// - Unified information architecture: Consistent layout across all notification types
/// - Accessible to everyone: Clear formatting, readable structure
/// </summary>
public static class NotificationHelper
{
    // ── Notification Types ────────────────────────────────────

    /// <summary>Severity level for in-game notifications.</summary>
    public enum NotificationLevel
    {
        Success,    // Green-tinted, positive outcome
        Info,       // Neutral information
        Warning,    // Caution, resolvable by user action
        Error,      // Problem that user can attempt to fix
        Critical    // System-level failure, user may need admin help
    }

    // ── Public API ────────────────────────────────────────────

    /// <summary>Send a structured notification to a player.</summary>
    public static void NotifyPlayer(User user, string message, NotificationLevel level = NotificationLevel.Info)
    {
        var formatted = FormatMessage(message, level);
        SendToUser(user, formatted);
    }

    /// <summary>
    /// Send a notification with a clear state label and optional troubleshooting guidance.
    /// Follows the "state name + action link" pattern from the Cloudflare redesign.
    /// </summary>
    public static void NotifyPlayerWithHelp(User user, string stateLabel, string summary, string? troubleshooting = null)
    {
        var formatted = FormatMessageWithHelp(stateLabel, summary, troubleshooting);
        SendToUser(user, formatted);
    }

    /// <summary>Broadcast a structured notification to all connected players.</summary>
    public static void NotifyAll(string message, NotificationLevel level = NotificationLevel.Info)
    {
        var formatted = FormatMessage(message, level);
        BroadcastToAll(formatted);
    }

    /// <summary>Send a structured notification to admin users only.</summary>
    public static void NotifyAdmins(string message, NotificationLevel level = NotificationLevel.Warning)
    {
        var formatted = FormatMessage(message, level);
        BroadcastToAdmins(formatted);
    }

    public static void NotifyAdminsRaw(string message)
    {
        BroadcastToAdmins(message);
    }

    /// <summary>Broadcast an already-formatted rich-text message without adding another prefix.</summary>
    public static void NotifyAllRaw(string message)
    {
        BroadcastToAll(message);
    }

    /// <summary>Send an already-formatted rich-text message to one player.</summary>
    public static void NotifyPlayerRaw(User user, string message)
    {
        SendToUser(user, message);
    }

    /// <summary>Wrap text in Unity rich-text color using #RRGGBB or a known color name.</summary>
    public static string ColorizeText(string message, string? color)
    {
        var normalized = NormalizeRgbColor(color);
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(message))
            return message;

        if (message.Contains("<color=", StringComparison.OrdinalIgnoreCase))
            return message;

        return $"<color={normalized}>{message}</color>";
    }

    public static NotificationLevel ParseLevel(string? value, NotificationLevel fallback = NotificationLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant() switch
        {
            "success" or "good" or "ok" or "ready" => NotificationLevel.Success,
            "warn" or "warning" or "caution" => NotificationLevel.Warning,
            "error" or "fail" or "failed" => NotificationLevel.Error,
            "critical" or "fatal" => NotificationLevel.Critical,
            "info" or "normal" => NotificationLevel.Info,
            _ => fallback
        };
    }

    public static string FormatAnnouncement(
        string message,
        string? color = null,
        string? title = null,
        NotificationLevel level = NotificationLevel.Info)
    {
        var body = string.IsNullOrWhiteSpace(title)
            ? message
            : $"{title.Trim()}: {message}";

        return FormatMessage(ColorizeText(body, color), level);
    }

    /// <summary>
    /// Notify a player that an operation completed successfully.
    /// Uses the validated "Verify → Verifying → Success" pattern.
    /// </summary>
    public static void NotifySuccess(User user, string actionCompleted)
    {
        NotifyPlayer(user, $"[BattleLuck] ✓ {actionCompleted}", NotificationLevel.Success);
    }

    /// <summary>
    /// Notify a player of an issue with actionable troubleshooting guidance.
    /// Replaces the old "Send Feedback" pattern with a "Troubleshoot" pattern.
    /// </summary>
    public static void NotifyTroubleshoot(User user, string issueLabel, string summary, string? actionToTry = null)
    {
        var msg = $"[BattleLuck] ⚠ {issueLabel}.\n  {summary}";
        if (!string.IsNullOrEmpty(actionToTry))
            msg += $"\n  Try: {actionToTry}";
        NotifyPlayer(user, msg, NotificationLevel.Warning);
    }

    // ── Formatting ────────────────────────────────────────────

    /// <summary>Format a message with severity-appropriate cue.</summary>
    static string FormatMessage(string message, NotificationLevel level)
    {
        var prefix = level switch
        {
            NotificationLevel.Success  => "✓",
            NotificationLevel.Info     => "•",
            NotificationLevel.Warning  => "⚠",
            NotificationLevel.Error    => "✗",
            NotificationLevel.Critical => "!!",
            _                          => "•"
        };

        // Keep the message scannable: prefix + concise text
        return ColorizeText($"[BattleLuck] {prefix} {message}", DefaultColor(level));
    }

    /// <summary>Format a message following the Cloudflare "state label + action" architecture.</summary>
    static string FormatMessageWithHelp(string stateLabel, string summary, string? troubleshooting)
    {
        var msg = $"[BattleLuck] {stateLabel}\n  {summary}";
        if (!string.IsNullOrEmpty(troubleshooting))
            msg += $"\n  Troubleshoot: {troubleshooting}";
        return ColorizeText(msg, DefaultColor(NotificationLevel.Warning));
    }

    static string DefaultColor(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "#47FF8A",
        NotificationLevel.Info => "#5CC8FF",
        NotificationLevel.Warning => "#FFD166",
        NotificationLevel.Error => "#FF5C7A",
        NotificationLevel.Critical => "#FF2E2E",
        _ => "#5CC8FF"
    };

    static string NormalizeRgbColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return "";

        var value = color.Trim();
        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
        {
            var inner = value[4..^1];
            var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3 &&
                byte.TryParse(parts[0], out var r) &&
                byte.TryParse(parts[1], out var g) &&
                byte.TryParse(parts[2], out var b))
            {
                return $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        if (!value.StartsWith("#", StringComparison.Ordinal))
            value = value.ToLowerInvariant() switch
            {
                "green" or "good" or "success" => "#47FF8A",
                "blue" or "info" => "#5CC8FF",
                "yellow" or "warning" => "#FFD166",
                "orange" or "event" => "#FFB347",
                "red" or "error" or "bad" => "#FF5C7A",
                "purple" or "admin" => "#C77DFF",
                "cyan" or "ai" => "#66E3FF",
                "white" => "#FFFFFF",
                _ => value
            };

        if (value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit))
            return value.ToUpperInvariant();

        return "";
    }

    // ── Delivery ──────────────────────────────────────────────

    static void SendToUser(User user, string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var msg = (FixedString512Bytes)message;
            ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Failed to send to player: {ex.Message}");
        }
    }

    static void BroadcastToAll(string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<User>());
            var users = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < users.Length; i++)
            {
                try
                {
                    var user = em.GetComponentData<User>(users[i]);
                    if (user.IsConnected)
                    {
                        var msg = (FixedString512Bytes)message;
                        ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
                    }
                }
                catch { /* skip disconnected */ }
            }
            users.Dispose();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Broadcast failed: {ex.Message}");
        }
    }

    static void BroadcastToAdmins(string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<User>());
            var users = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < users.Length; i++)
            {
                try
                {
                    var user = em.GetComponentData<User>(users[i]);
                    if (user.IsConnected && user.IsAdmin)
                    {
                        var msg = (FixedString512Bytes)message;
                        ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
                    }
                }
                catch { /* skip */ }
            }
            users.Dispose();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Admin notify failed: {ex.Message}");
        }
    }
}
