using BattleLuck.Commands;
using BattleLuck.Models;
using BattleLuck.Services.Assistant;

namespace BattleLuck.Commands.Admin;

public static class PlatformAdminCommands
{
    static readonly LocalModelAdminService Models = new();
    [BattleLuckCommand("bl admin diagnostics", description: "Show server-platform health", adminOnly: true)]
    public static void Diagnostics(BattleLuckCommandContext ctx)
    {
        var events = BattleLuckPlugin.EventPlatform;
        var errors = BattleLuckPlugin.ErrorReporter.GetDiagnostics();
        var performance = events?.GetDiagnostics();
        ctx.Reply($"BattleLuck {BattleLuckPluginInfo.PluginVersion}: core={BattleLuckPlugin.IsInitialized}; " +
                  $"eventPlatform={(events == null ? "offline" : "ready")}; backtrace={(errors.Enabled ? "ready" : errors.DisabledReason ?? "disabled")}; " +
                  $"eventAvgMs={performance?.AverageMilliseconds ?? 0:F3}; eventP99Ms={performance?.P99Milliseconds ?? 0:F3}; " +
                  $"errorQueue={errors.Queued}; submitted={errors.Submitted}; dropped={errors.Dropped}; deduplicated={errors.Deduplicated}.");
    }

    [BattleLuckCommand("bl admin diagnostics errors", description: "Show error-reporter counters", adminOnly: true)]
    public static void Errors(BattleLuckCommandContext ctx)
    {
        var value = BattleLuckPlugin.ErrorReporter.GetDiagnostics();
        ctx.Reply($"Backtrace enabled={value.Enabled}; queued={value.Queued}; submitted={value.Submitted}; " +
                  $"dropped={value.Dropped}; deduplicated={value.Deduplicated}; retried={value.Retried}; reason={value.DisabledReason ?? "none"}.");
    }

    [BattleLuckCommand("bl admin diagnostics errors test", description: "Queue a synthetic BattleLuck error", adminOnly: true)]
    public static void TestError(BattleLuckCommandContext ctx)
    {
        var before = BattleLuckPlugin.ErrorReporter.GetDiagnostics();
        if (!before.Enabled) { ctx.Reply($"Backtrace is not active: {before.DisabledReason ?? "disabled"}."); return; }
        BattleLuckPlugin.ErrorReporter.Report(new InvalidOperationException("BattleLuck administrator diagnostic test"),
            new ErrorReportContext { AdminSteamId = ctx.SenderSteamId, Command = ".bl admin diagnostics errors test", Critical = false });
        ctx.Reply("Synthetic BattleLuck error queued. No game or Unity error was generated.");
    }

    [BattleLuckCommand("bl admin config status", description: "Show owner configuration status", adminOnly: true)]
    public static void ConfigStatus(BattleLuckCommandContext ctx)
    {
        var value = ConfigLoader.LoadBattleLuckConfig();
        ctx.Reply($"battleluck.json schema={value.Schema}; events={value.Events.Enabled}; killfeed={value.Chat.KillfeedScope}; " +
                  $"results.keep={value.Results.Keep}; assistant={value.Assistant.Mode}/{value.Assistant.Model}; backtrace={value.Backtrace.Enabled}.");
    }

    [BattleLuckCommand("bl admin config reload", description: "Validate and reload owner configuration", adminOnly: true)]
    public static void ConfigReload(BattleLuckCommandContext ctx)
    {
        var success = ConfigLoader.TryReloadBattleLuckConfig(out var message);
        ctx.Reply((success ? "✔ " : "✘ ") + message);
    }

    [BattleLuckCommand("bl admin ai status", description: "Check the optional local LLM", adminOnly: true)]
    public static async Task AiStatus(BattleLuckCommandContext ctx)
    {
        var config = ConfigLoader.LoadBattleLuckConfig().Assistant;
        var result = await Models.StatusAsync(new Uri(config.LocalUrl), config.Model).ConfigureAwait(false);
        BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, result);
    }

    [BattleLuckCommand("bl admin ai install-model", description: "Explicitly install the allowlisted local model", adminOnly: true)]
    public static async Task InstallModel(BattleLuckCommandContext ctx)
    {
        var config = ConfigLoader.LoadBattleLuckConfig().Assistant;
        ctx.Reply($"Requesting explicit Ollama installation of {config.Model}; gameplay continues on AI-lite.");
        var result = await Models.InstallAsync(new Uri(config.LocalUrl), config.Model).ConfigureAwait(false);
        BattleLuckPlugin.NotifyPlayerBySteamIdOnMainThread(ctx.SenderSteamId, result);
    }
}
