using BattleLuck.Models;
using ProjectM.CastleBuilding;

namespace BattleLuck.Services.Castles;

// ─────────────────────────────────────────────────────────────────────────────
// CastleInteractionRouter
//
// Single entry point that game-side hooks (storage open, item take, item give,
// stack transfer) call into. The router:
//
//   1. Resolves the live entity to a stable CastleObjectKey.
//   2. Finds the policy that targets that key.
//   3. Evaluates the policy with the requester's identity.
//   4. (If cost is enabled) prepares a payment reservation.
//   5. Returns a CastleAccessDecision to the hook.
//
// The hook then performs the native game interaction and reports success
// or failure to the router, which commits or cancels the reservation.
// This keeps every castle-object mutation under the same authorization
// and atomic-payment rules regardless of which object type the hook
// targets.
//
// The router does NOT do object discovery for kind-specific prefabs in
// this initial release — kind dispatch lives in release A/B/C hooks that
// will be added incrementally. The router is the canonical seam where
// those hooks converge.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CastleInteractionRouter
{
    readonly CastlePolicyService _service;

    public CastleInteractionRouter(CastlePolicyService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public CastlePolicyService Service => _service;

    /// <summary>
    /// Resolve a live entity to the policy that targets it, or null if no
    /// policy exists for this object. Used by hooks to short-circuit
    /// when the owner has not configured access rules.
    /// </summary>
    public CastleObjectPolicy? FindPolicyForEntity(Entity entity)
    {
        if (!entity.Exists() || !_service.Resolver.IsWorldReady) return null;
        try
        {
            // Build a tentative key from the entity and search the store.
            var key = BuildKeyFromEntity(entity);
            if (key == null) return null;
            var matches = _service.Store.FindByTarget(key);
            return matches.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public CastleAccessDecision EvaluateForEntity(
        Entity entity,
        ulong requesterSteamId,
        bool isAdmin,
        DateTime utc)
    {
        var policy = FindPolicyForEntity(entity);
        if (policy == null)
        {
            // No policy: fall through to the "is the requester the owner"
            // check. Owners can always use their own objects.
            var key = BuildKeyFromEntity(entity);
            var liveOwner = key == null ? 0 : _service.Resolver.ResolveOwner(key, entity);
            if (isAdmin || (liveOwner != 0 && liveOwner == requesterSteamId))
                return CastleAccessDecision.Allow();
            return CastleAccessDecision.Deny("No policy is set for this object.", "No policy", "The owner must create a policy with `.castlepolicy target`.");
        }
        return _service.Evaluate(policy, requesterSteamId, utc, isAdmin);
    }

    public OperationResult<CastlePaymentReservation> PreparePayment(
        ulong requesterSteamId,
        string policyId,
        DateTime utc)
    {
        return _service.Payments.Prepare(requesterSteamId, policyId, utc);
    }

    public OperationResult CommitPayment(string reservationId, DateTime utc)
    {
        return _service.Payments.Commit(reservationId, utc);
    }

    public OperationResult CancelPayment(string reservationId, string reason)
    {
        return _service.Payments.Cancel(reservationId, reason);
    }

    static CastleObjectKey? BuildKeyFromEntity(Entity entity)
    {
        if (!entity.Exists()) return null;
        try
        {
            if (!entity.Has<PrefabGUID>() || !entity.Has<Translation>()) return null;
            var em = VRisingCore.EntityManager;
            var guid = entity.Read<PrefabGUID>();
            var pos = entity.Read<Translation>().Value;
            var owner = 0UL;
            var heartHash = 0;
            if (entity.Has<CastleHeartConnection>())
            {
                var heart = entity.Read<CastleHeartConnection>().CastleHeartEntity.GetEntityOnServer();
                if (heart.Exists())
                {
                    if (heart.Has<UserOwner>())
                        owner = heart.Read<UserOwner>().Owner.GetEntityOnServer().GetSteamId();
                    else if (heart.Has<CastleHeart>())
                        owner = heart.Read<CastleHeart>().LastUserOwner.GetEntityOnServer().GetSteamId();
                    if (heart.Has<PrefabGUID>())
                        heartHash = heart.Read<PrefabGUID>().GuidHash;
                }
            }
            return new CastleObjectKey
            {
                OwnerSteamId = owner,
                CastleHeartPrefabHash = heartHash,
                ObjectPrefabHash = guid.GuidHash,
                MapIndex = -1,
                LocalPosition = new QuantizedPosition { X = pos.x, Y = pos.y, Z = pos.z }
            };
        }
        catch
        {
            return null;
        }
    }
}
