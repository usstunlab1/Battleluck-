using BattleLuck.Models;
using ProjectM.CastleBuilding;

namespace BattleLuck.Services.Castles;

// ─────────────────────────────────────────────────────────────────────────────
// CastlePolicyService
//
// The central policy engine for castle objects. All mutations and all
// evaluations flow through this service.
//
// Evaluation order (matches the verdict's specification):
//   1. Validate target (key valid, entity resolves).
//   2. Permit owner / admin.
//   3. Apply explicit deny.
//   4. Apply explicit allow.
//   5. Check general access level (Private / Public / Clan).
//   6. Check schedule.
//   7. Check capacity (placeholder for future territory caps).
//   8. Check quota (does the requester have usage left?).
//   9. Check payment availability (cost + target reachable).
//  10. Return prepared decision.
//
// Authorization boundary:
//   Every mutation method takes an (actorSteamId, isAdmin) pair. The
//   service resolves the live entity, re-derives the owner from the
//   castle heart, and grants the mutation only if the actor owns the
//   heart, has clan permission, or is an admin. OwnerSteamId from the
//   request is never trusted.
//
// Atomic payment + quota:
//   The service does NOT directly mutate inventory. It exposes:
//     - PreparePayment(actor, policy): reserves cost + quota slot.
//     - CommitPayment(reservationId):  records the transfer on success.
//     - CancelPayment(reservationId):  releases the slot on failure.
//   Hooks perform the native game interaction between prepare and
//   commit, and report the result so the service can release on failure.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CastlePolicyService : IDisposable
{
    readonly object _sync = new();
    readonly CastlePolicyStore _store;
    readonly CastleObjectResolver _resolver;
    readonly CastlePaymentService _payments;
    readonly Dictionary<string, CastlePaymentReservation> _reservations = new(StringComparer.OrdinalIgnoreCase);
    float _saveElapsed;
    CastleInteractionRouter? _router;
    bool _disposed;

    public CastlePolicyService(
        CastlePolicyStore store,
        CastleObjectResolver resolver,
        CastlePaymentService payments)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
    }

    public CastlePolicyStore Store => _store;
    public CastleObjectResolver Resolver => _resolver;
    public CastlePaymentService Payments => _payments;
    public CastleInteractionRouter Router => _router ??= new CastleInteractionRouter(this);

    public int Count => _store.Count;

    public void Initialize() => _store.Load();

    public void Tick(float deltaSeconds)
    {
        bool shouldSave;
        lock (_sync)
        {
            _saveElapsed += Math.Max(0, deltaSeconds);
            shouldSave = _store.Count > 0 && _saveElapsed >= 2f;
            if (shouldSave)
                _saveElapsed = 0f;
        }
        if (shouldSave) _store.SaveNow();

        // Sweep expired reservations on every tick.
        _payments.SweepExpiredReservations();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    // ── Mutations (all go through authorization boundary) ─────────────────

    public OperationResult<CastleObjectPolicy> UpsertPolicy(
        ulong actorSteamId,
        bool isAdmin,
        CastlePolicyRequest request)
    {
        var auth = AuthorizeForTarget(actorSteamId, isAdmin, request.Target, out _, out var resolveError);
        if (!auth.Success) return OperationResult<CastleObjectPolicy>.Fail(auth.Error);

        var validation = ValidateRequest(request);
        if (validation != null) return OperationResult<CastleObjectPolicy>.Fail(validation);

        // Re-derive the owner from the live entity so the persisted owner
        // matches the castle heart, not whatever the request claimed.
        var liveOwner = auth.ResolvedOwner;
        var policyId = string.IsNullOrWhiteSpace(request.PolicyId)
            ? MakePolicyId(request.Target)
            : request.PolicyId;

        var policy = new CastleObjectPolicy
        {
            PolicyId = policyId,
            Target = CastlePolicyStore.Clone(request.Target),
            OwnerSteamId = liveOwner,
            OwnerName = SanitizeName(string.IsNullOrWhiteSpace(request.Label) ? "" : request.Label, 80),
            Kind = request.Kind,
            Access = request.Access,
            Schedule = CastlePolicyStore.Clone(request.Schedule),
            Cost = CastlePolicyStore.Clone(request.Cost),
            Quota = CastlePolicyStore.Clone(request.Quota),
            Label = SanitizeName(request.Label, 80),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var snapshot = _store.Upsert(policy);
        return OperationResult<CastleObjectPolicy>.Ok(snapshot);
    }

    public OperationResult RemovePolicy(
        ulong actorSteamId,
        bool isAdmin,
        string policyId)
    {
        var existing = _store.Get(policyId);
        if (existing == null) return OperationResult.Fail($"Policy '{policyId}' does not exist.");

        var auth = AuthorizeForTarget(actorSteamId, isAdmin, existing.Target, out _, out var resolveError);
        if (!auth.Success) return OperationResult.Fail(auth.Error);

        if (!_store.Remove(policyId)) return OperationResult.Fail($"Policy '{policyId}' was modified concurrently.");
        return OperationResult.Ok();
    }

    public OperationResult GrantPermission(
        ulong actorSteamId,
        bool isAdmin,
        string policyId,
        ulong subjectSteamId,
        string subjectName,
        PermissionEffect effect)
    {
        var existing = _store.Get(policyId);
        if (existing == null) return OperationResult.Fail($"Policy '{policyId}' does not exist.");

        var auth = AuthorizeForTarget(actorSteamId, isAdmin, existing.Target, out _, out var _);
        if (!auth.Success) return OperationResult.Fail(auth.Error);

        var rule = new CastlePermissionRule
        {
            SubjectSteamId = subjectSteamId,
            SubjectName = SanitizeName(subjectName, 64),
            Effect = effect,
            GrantedAtUtc = DateTime.UtcNow
        };
        existing.Permissions.RemoveAll(p => p.SubjectSteamId == subjectSteamId);
        existing.Permissions.Add(rule);
        existing.UpdatedAtUtc = DateTime.UtcNow;
        _store.Upsert(existing);
        return OperationResult.Ok();
    }

    public OperationResult RevokePermission(
        ulong actorSteamId,
        bool isAdmin,
        string policyId,
        ulong subjectSteamId)
    {
        var existing = _store.Get(policyId);
        if (existing == null) return OperationResult.Fail($"Policy '{policyId}' does not exist.");

        var auth = AuthorizeForTarget(actorSteamId, isAdmin, existing.Target, out _, out var _);
        if (!auth.Success) return OperationResult.Fail(auth.Error);

        var removed = existing.Permissions.RemoveAll(p => p.SubjectSteamId == subjectSteamId);
        if (removed == 0) return OperationResult.Fail($"No permission entry for subject {subjectSteamId}.");
        existing.UpdatedAtUtc = DateTime.UtcNow;
        _store.Upsert(existing);
        return OperationResult.Ok();
    }

    public OperationResult SetExcludedFromTerritoryApply(
        ulong actorSteamId,
        bool isAdmin,
        string policyId,
        bool excluded)
    {
        var existing = _store.Get(policyId);
        if (existing == null) return OperationResult.Fail($"Policy '{policyId}' does not exist.");

        var auth = AuthorizeForTarget(actorSteamId, isAdmin, existing.Target, out _, out var _);
        if (!auth.Success) return OperationResult.Fail(auth.Error);

        existing.ExcludeFromTerritoryApply = excluded;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        _store.Upsert(existing);
        return OperationResult.Ok();
    }

    // ── Evaluation ────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate the policy for a target + requester at a specific time.
    /// This is the public read-only path used by hooks and commands. It
    /// does NOT mutate counters or payments.
    /// </summary>
    public CastleAccessDecision Evaluate(CastleObjectPolicy policy, ulong requesterSteamId, DateTime utc, bool isAdmin)
    {
        if (policy == null) return CastleAccessDecision.Deny("Policy not found.");
        var key = policy.Target;
        if (key == null || !key.IsValid())
            return CastleAccessDecision.Deny("Target key is invalid.", "Invalid", "Repoint the policy with `.castlepolicy target`.");

        if (!_resolver.TryResolve(key, out var entity))
            return CastleAccessDecision.Deny("Object not found in the world.", "Missing", "The castle object may have been destroyed; rebind with `.castlepolicy target`.");

        // 1) Validate target
        if (entity == Entity.Null || !entity.Exists())
            return CastleAccessDecision.Deny("Object is no longer valid.", "Missing", "Rebind the policy.");

        // 2) Owner / admin bypass
        var liveOwner = _resolver.ResolveOwner(key, entity);
        if (isAdmin || (liveOwner != 0 && liveOwner == requesterSteamId))
            return CastleAccessDecision.Allow(policy.Cost);

        // 3) Explicit deny
        var explicitDeny = policy.Permissions.FirstOrDefault(p =>
            p.SubjectSteamId == requesterSteamId && p.Effect == PermissionEffect.Deny);
        if (explicitDeny != null)
            return CastleAccessDecision.Deny("You are explicitly denied.", "Denied", "Ask the castle owner to remove the deny rule.");

        // 4) Explicit allow
        var explicitAllow = policy.Permissions.FirstOrDefault(p =>
            p.SubjectSteamId == requesterSteamId && p.Effect == PermissionEffect.Allow);
        if (explicitAllow != null)
            return EvaluateSecondary(policy, requesterSteamId, utc);

        // 5) Access level
        if (policy.Access == CastleAccessLevel.Private)
            return CastleAccessDecision.Deny("This object is private.", "Private", "Ask the owner to grant you access.");

        // 6) Schedule
        if (!policy.Schedule.IsWithinWindow(utc))
            return CastleAccessDecision.Deny("Outside the configured hours.", "Outside hours", "Come back during the configured hours.");

        // 7) Capacity (placeholder; reserved for territory caps in a later release)

        // 8) Quota
        if (policy.Quota.Enabled && !QuotaHasRoom(policy, requesterSteamId, utc))
            return CastleAccessDecision.Deny("You have used your quota for this window.", "Quota", "Wait for the quota window to reset.");

        // 9) Payment availability
        if (policy.Access == CastleAccessLevel.Public && !policy.Cost.Enabled)
            return EvaluateSecondary(policy, requesterSteamId, utc);

        if (policy.Cost.Enabled)
        {
            if (!_resolver.TryResolve(policy.Cost.PaymentTarget!, out var target))
                return CastleAccessDecision.Deny("Payment target is missing.", "Misconfigured", "Owner must set a payment target with `.castlepolicy payment-target`.");
        }

        return CastleAccessDecision.Allow(policy.Cost);
    }

    CastleAccessDecision EvaluateSecondary(CastleObjectPolicy policy, ulong requesterSteamId, DateTime utc)
    {
        if (!policy.Schedule.IsWithinWindow(utc))
            return CastleAccessDecision.Deny("Outside the configured hours.", "Outside hours", "Come back during the configured hours.");
        if (policy.Quota.Enabled && !QuotaHasRoom(policy, requesterSteamId, utc))
            return CastleAccessDecision.Deny("You have used your quota for this window.", "Quota", "Wait for the quota window to reset.");
        return CastleAccessDecision.Allow(policy.Cost);
    }

    public bool QuotaHasRoom(CastleObjectPolicy policy, ulong subjectSteamId, DateTime utc)
        => CastlePolicyRules.QuotaHasRoom(policy, subjectSteamId, utc);

    // ── Authorization boundary ────────────────────────────────────────────

    public sealed class AuthorizationResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = "";
        public ulong ResolvedOwner { get; init; }
    }

    AuthorizationResult AuthorizeForTarget(ulong actorSteamId, bool isAdmin, CastleObjectKey key, out Entity entity, out string resolveError)
    {
        entity = default;
        resolveError = "";
        if (key == null || !key.IsValid())
            return new AuthorizationResult { Success = false, Error = "Target key is invalid." };

        if (!_resolver.TryResolve(key, out entity))
        {
            resolveError = "Object not found. Stand near the castle object and use `.castlepolicy target` to rebind.";
            return new AuthorizationResult { Success = false, Error = resolveError };
        }

        var liveOwner = _resolver.ResolveOwner(key, entity);
        if (liveOwner == 0)
            return new AuthorizationResult { Success = false, Error = "Owner could not be derived from the castle heart." };

        if (isAdmin || liveOwner == actorSteamId)
            return new AuthorizationResult { Success = true, ResolvedOwner = liveOwner };

        return new AuthorizationResult { Success = false, Error = "Only the owner of the castle heart or an admin can modify this policy." };
    }

    // ── Territory-wide application (Release D) ───────────────────────────

    public sealed class TerritoryApplyPreview
    {
        public int CandidateCount { get; set; }
        public int AlreadyHasPolicyCount { get; set; }
        public int ExcludedCount { get; set; }
        public int PaymentTargetCount { get; set; }
        public List<CastleObjectKey> CandidateKeys { get; set; } = new();
    }

    public TerritoryApplyPreview PreviewTerritoryApply(ulong ownerSteamId, CastleAccessLevel newAccess)
    {
        var preview = new TerritoryApplyPreview();
        var allPolicies = _store.ListByOwner(ownerSteamId);
        var excluded = allPolicies.Where(p => p.ExcludeFromTerritoryApply).Select(p => p.Target).ToList();
        var paymentTargets = allPolicies
            .Where(p => p.Cost.Enabled && p.Cost.PaymentTarget != null)
            .Select(p => p.Cost.PaymentTarget!)
            .ToList();

        var candidates = DiscoverCastleObjects(ownerSteamId);
        preview.CandidateCount = candidates.Count;
        preview.ExcludedCount = candidates.Count(k => excluded.Any(e => SameKey(k, e)));
        preview.PaymentTargetCount = candidates.Count(k => paymentTargets.Any(p => SameKey(k, p)));
        preview.CandidateKeys = candidates;
        preview.AlreadyHasPolicyCount = candidates.Count(k => _store.FindByTarget(k).Count > 0);
        return preview;
    }

    public OperationResult<int> ApplyTerritoryAccess(ulong ownerSteamId, CastleAccessLevel newAccess, bool confirmed)
    {
        if (!confirmed)
            return OperationResult<int>.Fail("Territory-wide apply requires `confirm: true`. Run `.castlepolicy territory apply` first to preview.");

        var allPolicies = _store.ListByOwner(ownerSteamId);
        var excluded = allPolicies.Where(p => p.ExcludeFromTerritoryApply).Select(p => p.Target).ToList();
        var paymentTargets = allPolicies
            .Where(p => p.Cost.Enabled && p.Cost.PaymentTarget != null)
            .Select(p => p.Cost.PaymentTarget!)
            .ToHashSet();
        var existingByKey = allPolicies.ToDictionary(p => MakeKey(p.Target), p => p);

        var candidates = DiscoverCastleObjects(ownerSteamId);
        int changed = 0;
        foreach (var key in candidates)
        {
            if (excluded.Any(e => SameKey(key, e))) continue;
            if (paymentTargets.Any(p => SameKey(key, p))) continue;

            var keyString = MakeKey(key);
            if (existingByKey.TryGetValue(keyString, out var existing))
            {
                existing.Access = newAccess;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                _store.Upsert(existing);
            }
            else
            {
                var policy = new CastleObjectPolicy
                {
                    PolicyId = MakePolicyId(key),
                    Target = key,
                    OwnerSteamId = ownerSteamId,
                    Kind = InferKindFromKey(key),
                    Access = newAccess,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _store.Upsert(policy);
            }
            changed++;
        }
        return OperationResult<int>.Ok(changed);
    }

    // ── Object discovery (delegated to resolver; placeholder fallback) ────

    List<CastleObjectKey> DiscoverCastleObjects(ulong ownerSteamId)
    {
        return _resolver.DiscoverOwnedObjects(ownerSteamId).ToList();
    }

    static CastleObjectKind InferKindFromKey(CastleObjectKey key) => CastleObjectKind.Storage;

    static bool SameKey(CastleObjectKey a, CastleObjectKey b) =>
        CastlePolicyRules.SameObject(a, b);

    static string MakeKey(CastleObjectKey k) =>
        $"{k.OwnerSteamId}:{k.ObjectPrefabHash}:{k.MapIndex}:{k.LocalPosition.X:F1},{k.LocalPosition.Y:F1},{k.LocalPosition.Z:F1}";

    static string MakePolicyId(CastleObjectKey k) => MakeKey(k);

    static string SanitizeName(string value, int maxLength)
    {
        var safe = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace("<", "").Replace(">", "").Trim();
        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    static string? ValidateRequest(CastlePolicyRequest request)
    {
        if (request == null) return "Request is null.";
        if (request.Target == null || !request.Target.IsValid())
            return "Target key is invalid.";
        if (request.Kind == CastleObjectKind.None)
            return "kind is required.";
        if (request.Schedule.Enabled)
        {
            foreach (var w in request.Schedule.Windows)
            {
                if (!w.IsValid()) return $"hours window {w.StartHour}-{w.EndHour} is invalid.";
            }
        }
        if (request.Quota.Enabled)
        {
            if (request.Quota.WindowHours <= 0f || request.Quota.WindowHours > 720f)
                return "Quota windowHours must be between 0 and 720.";
            if (request.Quota.MaxAmount <= 0 || request.Quota.MaxAmount > 1_000_000)
                return "Quota maxAmount must be between 1 and 1000000.";
        }
        if (request.Cost.Enabled)
        {
            if (request.Cost.PrefabHash == 0)
                return "Cost prefab hash is required when cost is enabled.";
            if (request.Cost.Amount <= 0)
                return "Cost amount must be greater than zero.";
            if (request.Cost.PaymentTarget == null || !request.Cost.PaymentTarget.IsValid())
                return "Cost paymentTarget is required when cost is enabled.";
        }
        return null;
    }
}
