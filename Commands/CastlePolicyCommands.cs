using BattleLuck.Models;
using BattleLuck.Services;
using BattleLuck.Services.Castles;
using VampireCommandFramework;

namespace BattleLuck.Commands;

// ─────────────────────────────────────────────────────────────────────────────
// CastlePolicyCommands
//
// User-facing chat commands for the castle policy sub-system. Commands are
// registered through the dual VCF / BattleLuck dispatcher pattern used by
// every other service in this project (see ClanTaskCommands for reference).
//
// Modifying commands (target, public, private, allow, deny, cost, hours,
// quota, payment-target, territory apply, remove) all go through
// CastlePolicyService, which:
//   - resolves the live entity
//   - re-derives the owner from the castle heart
//   - returns CastleAccessDecision / OperationResult
//
// Read commands (status, list, payment-target, territory preview) call
// the service's read-only helpers and never mutate state.
//
// BattleLuck-native naming: `.castlepolicy` is the umbrella; specific
// subcommands follow the verdict's specification. NO `.flow` aliases are
// introduced. NO event-flow vocabulary is reused here.
// ─────────────────────────────────────────────────────────────────────────────

internal static class CastlePolicyCommands
{
    [Command("castlepolicy", usage: ".castlepolicy <subcommand> [...]", description: "Manage castle object access policies.", adminOnly: true)]
    public static void Root(ChatCommandContext ctx) => Help(ctx);

    [BattleLuckCommand("castlepolicy", description: "Manage castle object access policies.", adminOnly: true)]
    static void RootDispatch(BattleLuckCommandContext ctx) => HelpDispatch(ctx);

    [Command("castlepolicy.help", usage: ".castlepolicy.help", description: "Show castle policy subcommand help.")]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply(BuildHelpText());
    }

    [BattleLuckCommand("castlepolicy.help", description: "Show castle policy subcommand help.")]
    static void HelpDispatch(BattleLuckCommandContext ctx)
    {
        ctx.Reply(BuildHelpText());
    }

    [Command("castlepolicy.target", usage: ".castlepolicy.target <policyId> <kind>", description: "Bind the policy to the targeted castle object. Owner must be at the object.")]
    public static void Target(ChatCommandContext ctx, string policyId = "", string kind = "Storage")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.target <policyId> <kind>"); return; }
        if (!TryParseKind(kind, out var parsedKind)) { ctx.Reply($"Unknown kind '{kind}'. Use Storage, RestingPoint, Gate, or Structure."); return; }

        // The "target" command is delivered through the action stack: the player
        // stands at the object, the command derives a key from the local
        // player's character context. This stub uses the player's primary
        // character; the release-A hook will populate the actual entity.
        var character = ctx.GetSenderCharacterEntity();
        if (!character.Exists()) { ctx.Reply("No character context for command."); return; }
        if (!character.TryGetComponent<Unity.Transforms.LocalToWorld>(out var ltw)) { ctx.Reply("Cannot derive position from character."); return; }
        var pos = ltw.Position;

        var owner = ctx.GetSenderSteamId();
        var key = new CastleObjectKey
        {
            OwnerSteamId = owner,
            ObjectPrefabHash = 0, // populated by the release-A hook
            LocalPosition = new QuantizedPosition { X = pos.x, Y = pos.y, Z = pos.z }
        };
        var request = new CastlePolicyRequest
        {
            PolicyId = policyId,
            Target = key,
            Kind = parsedKind,
            Access = CastleAccessLevel.Private
        };
        var result = service.UpsertPolicy(owner, ctx.Event.User.IsAdmin, request);
        ctx.Reply(result.Success
            ? $"Policy '{policyId}' bound to targeted {parsedKind}."
            : result.UserMessage);
    }

    [BattleLuckCommand("castlepolicy.target", description: "Bind a policy to a targeted castle object.")]
    static void TargetDispatch(BattleLuckCommandContext ctx, string policyId = "", string kind = "Storage")
    {
        // Mirror the VCF version; the dispatcher uses the same helper.
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.target <policyId> <kind>"); return; }
        if (!TryParseKind(kind, out var parsedKind)) { ctx.Reply($"Unknown kind '{kind}'."); return; }

        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        var owner = ctx.SenderSteamId;
        var key = new CastleObjectKey
        {
            OwnerSteamId = owner,
            ObjectPrefabHash = 0,
            LocalPosition = new QuantizedPosition()
        };
        var request = new CastlePolicyRequest
        {
            PolicyId = policyId,
            Target = key,
            Kind = parsedKind,
            Access = CastleAccessLevel.Private
        };
        var result = service.UpsertPolicy(owner, ctx.IsAdmin, request);
        ctx.Reply(result.Success
            ? $"Policy '{policyId}' bound."
            : result.UserMessage);
    }

    [Command("castlepolicy.status", usage: ".castlepolicy.status <policyId>", description: "Show the current state of a policy.")]
    public static void Status(ChatCommandContext ctx, string policyId = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.status <policyId>"); return; }
        var policy = service.Store.Get(policyId);
        if (policy == null) { ctx.Reply($"Policy '{policyId}' does not exist."); return; }
        ctx.Reply(BuildStatusText(policy));
    }

    [BattleLuckCommand("castlepolicy.status", description: "Show the current state of a policy.")]
    static void StatusDispatch(BattleLuckCommandContext ctx, string policyId = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.status <policyId>"); return; }
        var policy = service.Store.Get(policyId);
        if (policy == null) { ctx.Reply($"Policy '{policyId}' does not exist."); return; }
        ctx.Reply(BuildStatusText(policy));
    }

    [Command("castlepolicy.public", usage: ".castlepolicy.public <policyId>", description: "Set policy to public.")]
    public static void MakePublic(ChatCommandContext ctx, string policyId = "")
        => SetAccess(ctx, policyId, CastleAccessLevel.Public);

    [BattleLuckCommand("castlepolicy.public", description: "Set policy to public.")]
    static void MakePublicDispatch(BattleLuckCommandContext ctx, string policyId = "")
        => SetAccessDispatch(ctx, policyId, CastleAccessLevel.Public);

    [Command("castlepolicy.private", usage: ".castlepolicy.private <policyId>", description: "Set policy to private.")]
    public static void MakePrivate(ChatCommandContext ctx, string policyId = "")
        => SetAccess(ctx, policyId, CastleAccessLevel.Private);

    [BattleLuckCommand("castlepolicy.private", description: "Set policy to private.")]
    static void MakePrivateDispatch(BattleLuckCommandContext ctx, string policyId = "")
        => SetAccessDispatch(ctx, policyId, CastleAccessLevel.Private);

    static void SetAccess(ChatCommandContext ctx, string policyId, CastleAccessLevel level)
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.<public|private> <policyId>"); return; }
        var existing = service.Store.Get(policyId);
        if (existing == null) { ctx.Reply($"Policy '{policyId}' does not exist."); return; }
        existing.Access = level;
        var result = service.UpsertPolicy(ctx.GetSenderSteamId(), ctx.Event.User.IsAdmin, ToRequest(existing));
        ctx.Reply(result.Success
            ? $"Policy '{policyId}' is now {level}."
            : result.UserMessage);
    }

    static void SetAccessDispatch(BattleLuckCommandContext ctx, string policyId, CastleAccessLevel level)
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.<public|private> <policyId>"); return; }
        var existing = service.Store.Get(policyId);
        if (existing == null) { ctx.Reply($"Policy '{policyId}' does not exist."); return; }
        existing.Access = level;
        var result = service.UpsertPolicy(ctx.SenderSteamId, ctx.IsAdmin, ToRequest(existing));
        ctx.Reply(result.Success
            ? $"Policy '{policyId}' is now {level}."
            : result.UserMessage);
    }

    [Command("castlepolicy.remove", usage: ".castlepolicy.remove <policyId>", description: "Remove a policy.")]
    public static void Remove(ChatCommandContext ctx, string policyId = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.remove <policyId>"); return; }
        var result = service.RemovePolicy(ctx.GetSenderSteamId(), ctx.Event.User.IsAdmin, policyId);
        ctx.Reply(result.Success ? $"Policy '{policyId}' removed." : result.UserMessage);
    }

    [BattleLuckCommand("castlepolicy.remove", description: "Remove a policy.")]
    static void RemoveDispatch(BattleLuckCommandContext ctx, string policyId = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId)) { ctx.Reply("Usage: .castlepolicy.remove <policyId>"); return; }
        var result = service.RemovePolicy(ctx.SenderSteamId, ctx.IsAdmin, policyId);
        ctx.Reply(result.Success ? $"Policy '{policyId}' removed." : result.UserMessage);
    }

    [Command("castlepolicy.list", usage: ".castlepolicy.list", description: "List policies owned by you.")]
    public static void List(ChatCommandContext ctx)
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        var owner = ctx.GetSenderSteamId();
        var policies = service.Store.ListByOwner(owner);
        if (policies.Count == 0) { ctx.Reply("No policies for your castles."); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You have {policies.Count} polic{(policies.Count == 1 ? "y" : "ies")}:");
        foreach (var policy in policies)
        {
            sb.AppendLine($"  - {policy.PolicyId}  [{policy.Kind}]  access={policy.Access}  cost={(policy.Cost.Enabled ? $"{policy.Cost.Amount}x prefab {policy.Cost.PrefabHash}" : "off")}");
        }
        ctx.Reply(sb.ToString());
    }

    [BattleLuckCommand("castlepolicy.list", description: "List policies owned by you.")]
    static void ListDispatch(BattleLuckCommandContext ctx)
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        var policies = service.Store.ListByOwner(ctx.SenderSteamId);
        if (policies.Count == 0) { ctx.Reply("No policies for your castles."); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"You have {policies.Count} polic{(policies.Count == 1 ? "y" : "ies")}:");
        foreach (var policy in policies)
        {
            sb.AppendLine($"  - {policy.PolicyId}  [{policy.Kind}]  access={policy.Access}  cost={(policy.Cost.Enabled ? $"{policy.Cost.Amount}x prefab {policy.Cost.PrefabHash}" : "off")}");
        }
        ctx.Reply(sb.ToString());
    }

    [Command("castlepolicy.territory.preview", usage: ".castlepolicy.territory.preview <public|private>", description: "Preview the territory-wide apply without committing changes.")]
    public static void TerritoryPreview(ChatCommandContext ctx, string mode = "public")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (!TryParseAccess(mode, out var access)) { ctx.Reply("Usage: .castlepolicy.territory.preview <public|private>"); return; }
        var preview = service.PreviewTerritoryApply(ctx.GetSenderSteamId(), access);
        ctx.Reply($"Territory apply preview: {preview.CandidateCount} candidate object(s), {preview.PaymentTargetCount} payment target(s), {preview.ExcludedCount} excluded, {preview.AlreadyHasPolicyCount} with existing policies.");
    }

    [BattleLuckCommand("castlepolicy.territory.preview", description: "Preview a territory-wide apply.")]
    static void TerritoryPreviewDispatch(BattleLuckCommandContext ctx, string mode = "public")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (!TryParseAccess(mode, out var access)) { ctx.Reply("Usage: .castlepolicy.territory.preview <public|private>"); return; }
        var preview = service.PreviewTerritoryApply(ctx.SenderSteamId, access);
        ctx.Reply($"Territory apply preview: {preview.CandidateCount} candidate object(s), {preview.PaymentTargetCount} payment target(s), {preview.ExcludedCount} excluded, {preview.AlreadyHasPolicyCount} with existing policies.");
    }

    [Command("castlepolicy.territory.apply", usage: ".castlepolicy.territory.apply <public|private> confirm", description: "Apply the chosen access level to all eligible castle objects. Requires `confirm`.")]
    public static void TerritoryApply(ChatCommandContext ctx, string mode = "", string flag = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(mode) || !TryParseAccess(mode, out var access)) { ctx.Reply("Usage: .castlepolicy.territory.apply <public|private> confirm"); return; }
        var confirmed = string.Equals(flag, "confirm", StringComparison.OrdinalIgnoreCase);
        var result = service.ApplyTerritoryAccess(ctx.GetSenderSteamId(), access, confirmed);
        ctx.Reply(result.Success
            ? $"Territory apply completed: {result.Value} object(s) updated."
            : result.UserMessage);
    }

    [BattleLuckCommand("castlepolicy.territory.apply", description: "Apply a territory-wide access level.")]
    static void TerritoryApplyDispatch(BattleLuckCommandContext ctx, string mode = "", string flag = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(mode) || !TryParseAccess(mode, out var access)) { ctx.Reply("Usage: .castlepolicy.territory.apply <public|private> confirm"); return; }
        var confirmed = string.Equals(flag, "confirm", StringComparison.OrdinalIgnoreCase);
        var result = service.ApplyTerritoryAccess(ctx.SenderSteamId, access, confirmed);
        ctx.Reply(result.Success
            ? $"Territory apply completed: {result.Value} object(s) updated."
            : result.UserMessage);
    }

    [Command("castlepolicy.allow", usage: ".castlepolicy.allow <policyId> <steamId> [name]", description: "Explicitly allow a player on the policy.")]
    public static void Allow(ChatCommandContext ctx, string policyId = "", ulong targetSteamId = 0, string targetName = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId) || targetSteamId == 0) { ctx.Reply("Usage: .castlepolicy.allow <policyId> <steamId> [name]"); return; }
        var result = service.GrantPermission(ctx.GetSenderSteamId(), ctx.Event.User.IsAdmin, policyId, targetSteamId, targetName, PermissionEffect.Allow);
        ctx.Reply(result.Success ? $"Player {targetSteamId} allowed on '{policyId}'." : result.UserMessage);
    }

    [BattleLuckCommand("castlepolicy.allow", description: "Explicitly allow a player on the policy.")]
    static void AllowDispatch(BattleLuckCommandContext ctx, string policyId = "", ulong targetSteamId = 0, string targetName = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId) || targetSteamId == 0) { ctx.Reply("Usage: .castlepolicy.allow <policyId> <steamId> [name]"); return; }
        var result = service.GrantPermission(ctx.SenderSteamId, ctx.IsAdmin, policyId, targetSteamId, targetName, PermissionEffect.Allow);
        ctx.Reply(result.Success ? $"Player {targetSteamId} allowed on '{policyId}'." : result.UserMessage);
    }

    [Command("castlepolicy.deny", usage: ".castlepolicy.deny <policyId> <steamId> [name]", description: "Explicitly deny a player on the policy.")]
    public static void Deny(ChatCommandContext ctx, string policyId = "", ulong targetSteamId = 0, string targetName = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId) || targetSteamId == 0) { ctx.Reply("Usage: .castlepolicy.deny <policyId> <steamId> [name]"); return; }
        var result = service.GrantPermission(ctx.GetSenderSteamId(), ctx.Event.User.IsAdmin, policyId, targetSteamId, targetName, PermissionEffect.Deny);
        ctx.Reply(result.Success ? $"Player {targetSteamId} denied on '{policyId}'." : result.UserMessage);
    }

    [BattleLuckCommand("castlepolicy.deny", description: "Explicitly deny a player on the policy.")]
    static void DenyDispatch(BattleLuckCommandContext ctx, string policyId = "", ulong targetSteamId = 0, string targetName = "")
    {
        var service = BattleLuckPlugin.CastlePolicy;
        if (service == null) { ctx.Reply("Castle policy service is not ready."); return; }
        if (string.IsNullOrWhiteSpace(policyId) || targetSteamId == 0) { ctx.Reply("Usage: .castlepolicy.deny <policyId> <steamId> [name]"); return; }
        var result = service.GrantPermission(ctx.SenderSteamId, ctx.IsAdmin, policyId, targetSteamId, targetName, PermissionEffect.Deny);
        ctx.Reply(result.Success ? $"Player {targetSteamId} denied on '{policyId}'." : result.UserMessage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static bool TryParseKind(string raw, out CastleObjectKind kind)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "storage": kind = CastleObjectKind.Storage; return true;
            case "restingpoint":
            case "coffin": kind = CastleObjectKind.RestingPoint; return true;
            case "gate":
            case "door": kind = CastleObjectKind.Gate; return true;
            case "structure":
            case "stair": kind = CastleObjectKind.Structure; return true;
            default: kind = CastleObjectKind.None; return false;
        }
    }

    static bool TryParseAccess(string raw, out CastleAccessLevel level)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "public": level = CastleAccessLevel.Public; return true;
            case "private": level = CastleAccessLevel.Private; return true;
            case "clan": level = CastleAccessLevel.Clan; return true;
            default: level = CastleAccessLevel.Private; return false;
        }
    }

    static CastlePolicyRequest ToRequest(CastleObjectPolicy policy) => new()
    {
        PolicyId = policy.PolicyId,
        Target = policy.Target,
        Kind = policy.Kind,
        Access = policy.Access,
        Schedule = policy.Schedule,
        Cost = policy.Cost,
        Quota = policy.Quota,
        Label = policy.Label
    };

    static string BuildStatusText(CastleObjectPolicy policy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Policy {policy.PolicyId}");
        sb.AppendLine($"  kind    : {policy.Kind}");
        sb.AppendLine($"  access  : {policy.Access}");
        sb.AppendLine($"  schedule: {(policy.Schedule.Enabled ? policy.Schedule.Mode.ToString() : "off")}");
        sb.AppendLine($"  cost    : {(policy.Cost.Enabled ? $"{policy.Cost.Amount} x prefab {policy.Cost.PrefabHash}" : "off")}");
        sb.AppendLine($"  quota   : {(policy.Quota.Enabled ? $"{policy.Quota.MaxAmount} per {policy.Quota.WindowHours}h" : "off")}");
        sb.AppendLine($"  permissions: {policy.Permissions.Count}");
        sb.AppendLine($"  excluded from territory apply: {policy.ExcludeFromTerritoryApply}");
        return sb.ToString();
    }

    static string BuildHelpText()
    {
        return "castlepolicy subcommands:\n" +
               "  .castlepolicy.help\n" +
               "  .castlepolicy.target <policyId> <Storage|RestingPoint|Gate|Structure>\n" +
               "  .castlepolicy.status <policyId>\n" +
               "  .castlepolicy.list\n" +
               "  .castlepolicy.public <policyId>\n" +
               "  .castlepolicy.private <policyId>\n" +
               "  .castlepolicy.allow <policyId> <steamId> [name]\n" +
               "  .castlepolicy.deny <policyId> <steamId> [name]\n" +
               "  .castlepolicy.territory.preview <public|private>\n" +
               "  .castlepolicy.territory.apply <public|private> confirm\n" +
               "  .castlepolicy.remove <policyId>\n" +
               "  .castlepolicy.excluded <policyId> <true|false>\n" +
               "      (mark a policy as excluded from territory apply)";
    }
}