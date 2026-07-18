using BattleLuck.Commands;
using BattleLuck.Core;
using static BattleLuck.Core.Validation.ZoneValidator;

namespace BattleLuck.Commands.Admin;

public sealed class LlmAnalyticsCommands
{
    [BattleLuckCommand("llm", description: "LLM/analytics control surface. Usage: .llm validate|execute|status <json>", adminOnly: true)]
    public static void Llm(BattleLuckCommandContext ctx, string subCommand, string jsonPayload = "")
    {
        subCommand = subCommand.Trim().ToLowerInvariant();
        var reply = subCommand switch
        {
            "validate" => ValidateIntent(jsonPayload),
            "execute" => ExecuteIntent(ctx, jsonPayload),
            "status" => AnalyticsStatus(),
            _ => $"Unknown sub-command '{subCommand}'. Usage: .llm validate|execute|status <json>"
        };

        ctx.Reply(reply);
    }

    static string ValidateIntent(string jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
            return "Missing JSON payload.";

        if (!TryParseIntent(jsonPayload, out var intent, out var parseError))
            return $"JSON parse error: {parseError}";

        if (string.IsNullOrWhiteSpace(intent.ModeId))
            return "Intent is missing 'modeId'.";

        var config = ConfigLoader.Load(intent.ModeId);
        if (config == null)
            return $"Mode '{intent.ModeId}' not found.";

        var issues = new List<string>();
        issues.AddRange(ActionRegistryValidator.Validate(intent.ModeId, config));
        issues.AddRange(Validate(intent.ModeId, config));
        issues.AddRange(KitValidator.Validate(intent.ModeId, config));
        issues.AddRange(PrefabValidator.Validate(intent.ModeId, config));
        issues.AddRange(SchematicValidator.Validate(intent.ModeId, config));
        issues.AddRange(FlowValidator.Validate(intent.ModeId, config));
        issues.AddRange(AnalyticsValidator.Validate(intent.ModeId, config));

        if (issues.Count == 0)
            return $"✔ Intent valid for mode '{intent.ModeId}'. Ready for execution.";

        return $"✘ Intent validation failed for mode '{intent.ModeId}' with {issues.Count} issue(s):\n" + string.Join("\n", issues.Take(20));
    }

    static string ExecuteIntent(BattleLuckCommandContext ctx, string jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
            return "Missing JSON payload.";

        if (!TryParseIntent(jsonPayload, out var intent, out var parseError))
            return $"JSON parse error: {parseError}";

        if (BattleLuckPlugin.Session == null)
            return "Session controller is not initialized.";

        if (!TryResolveTarget(ctx, intent.Target ?? "self", out var target))
            return "Target resolution failed.";

        var steamId = target.GetSteamId();

        return intent.IntentType.ToLowerInvariant() switch
        {
            "join_zone" or "enter_zone" or "join" => ExecuteJoinZone(steamId, target, intent),
            "leave_zone" or "exit_zone" or "leave" => ExecuteLeaveZone(steamId, target),
            _ => $"Unknown intentType '{intent.IntentType}'. Expected: join_zone, leave_zone"
        };
    }

    static string ExecuteJoinZone(ulong steamId, Entity player, LlmIntent intent)
    {
        if (string.IsNullOrWhiteSpace(intent.ModeId))
            return "join_zone intent requires 'modeId'.";

        var result = BattleLuckPlugin.Session.ToggleEnter(steamId, player, intent.ModeId);
        return result.Success
            ? $"✔ Player joined mode '{intent.ModeId}'."
            : $"✘ Join failed: {result.Error ?? result.UserMessage}";
    }

    static string ExecuteLeaveZone(ulong steamId, Entity player, LlmIntent? intent = null)
    {
        var result = BattleLuckPlugin.Session.ToggleLeave(steamId, player);
        return result.Success
            ? "✔ Player left zone."
            : $"✘ Leave failed: {result.Error ?? result.UserMessage}";
    }

    static string AnalyticsStatus()
    {
        var modes = new[] { "bloodbath", "trials", "siege", "colosseum", "aievent", "llm_realtest" };
        var status = new List<string> { "Analytics Status:" };

        foreach (var modeId in modes)
        {
            var config = ConfigLoader.Load(modeId);
            if (config == null)
            {
                status.Add($"  {modeId}: not loaded");
                continue;
            }

            var issues = new List<string>();
            issues.AddRange(FlowValidator.Validate(modeId, config));
            issues.AddRange(AnalyticsValidator.Validate(modeId, config));

            var zoneCount = config.Zones?.Zones?.Count ?? 0;
            var enterActions = ResolveAllActions(config, "enter").Count;
            var exitActions = ResolveAllActions(config, "exit").Count;

            status.Add($"  {modeId}: {zoneCount} zone(s), enter={enterActions}, exit={exitActions}, issues={issues.Count}");
        }

        return string.Join("\n", status);
    }

    static bool TryParseIntent(string jsonPayload, out LlmIntent intent, out string error)
    {
        intent = new LlmIntent();
        error = "";

        try
        {
            var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            intent.IntentType = root.GetPropertyOrDefault("intentType", root.GetPropertyOrDefault("intent", "join_zone"));
            intent.ModeId = root.GetPropertyOrDefault("modeId", "");
            intent.Target = root.GetPropertyOrDefault("target", "self");
            intent.DryRun = root.GetPropertyOrDefault("dryRun", false);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool TryResolveTarget(BattleLuckCommandContext ctx, string selector, out Unity.Entities.Entity player)
    {
        player = Unity.Entities.Entity.Null;

        if (string.IsNullOrWhiteSpace(selector) || selector.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            player = ctx.SenderCharacterEntity;
            if (player.Exists())
                return true;
            ctx.Reply("Sender character entity is not available.");
            return false;
        }

        foreach (var candidate in VRisingCore.GetOnlinePlayers())
        {
            if (!candidate.Exists() || !candidate.IsPlayer())
                continue;

            var name = candidate.GetPlayerName();
            var steamId = candidate.GetSteamId();

            if ((ulong.TryParse(selector, System.Globalization.NumberStyles.Integer,
                     System.Globalization.CultureInfo.InvariantCulture, out var sid) && steamId == sid) ||
                name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(selector, StringComparison.OrdinalIgnoreCase))
            {
                player = candidate;
                return true;
            }
        }

        ctx.Reply($"No online player matched '{selector}'.");
        return false;
    }

    static List<string> ResolveAllActions(ModeConfig config, string phase)
    {
        var result = new List<string>();
        FlowConfig? flow = phase switch
        {
            "enter" => config.FlowEnter,
            "exit" => config.FlowExit,
            "start" => config.Session?.Flow?.Start,
            "tracking" => config.Session?.Flow?.Tracking,
            "winner" => config.Session?.Flow?.Winner,
            "ending" => config.Session?.Flow?.Ending,
            _ => null
        };

        if (flow?.Flows == null) return result;

        foreach (var flowDef in flow.Flows.Values)
        {
            if (flowDef.Actions != null)
                result.AddRange(flowDef.Actions);
        }

        return result;
    }
}

public sealed class LlmIntent
{
    public string IntentType { get; set; } = "join_zone";
    public string ModeId { get; set; } = "";
    public string Target { get; set; } = "self";
    public bool DryRun { get; set; }
    public List<string> Actions { get; set; } = new();
}

public static class JsonElementExtensions
{
    public static T GetPropertyOrDefault<T>(this JsonElement element, string name, T fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;

        try
        {
            if (typeof(T) == typeof(string))
                return (T)(object)(value.GetString() ?? string.Empty);

            if (typeof(T) == typeof(int))
                return (T)(object)value.GetInt32();

            if (typeof(T) == typeof(bool))
                return (T)(object)value.GetBoolean();

            if (typeof(T) == typeof(double) || typeof(T) == typeof(float))
                return (T)(object)value.GetSingle();

            return fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
