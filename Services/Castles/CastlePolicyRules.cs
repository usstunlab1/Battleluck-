using BattleLuck.Models;

namespace BattleLuck.Services.Castles;

public static class CastlePolicyRules
{
    public static bool QuotaHasRoom(
        CastleObjectPolicy policy,
        ulong subjectSteamId,
        DateTime utc)
    {
        if (policy == null || !policy.Quota.Enabled)
            return true;

        var counter = policy.QuotaCounters.FirstOrDefault(item => item.SubjectSteamId == subjectSteamId);
        if (counter == null)
            return true;

        var windowHours = policy.Quota.WindowHours > 0f ? policy.Quota.WindowHours : 24f;
        return (utc - counter.WindowStartUtc).TotalHours >= windowHours ||
               counter.Count < policy.Quota.MaxAmount;
    }

    public static bool SameObject(CastleObjectKey a, CastleObjectKey b) =>
        a.OwnerSteamId == b.OwnerSteamId &&
        a.ObjectPrefabHash == b.ObjectPrefabHash &&
        a.MapIndex == b.MapIndex &&
        Math.Abs(a.LocalPosition.X - b.LocalPosition.X) < 0.5f &&
        Math.Abs(a.LocalPosition.Y - b.LocalPosition.Y) < 0.5f &&
        Math.Abs(a.LocalPosition.Z - b.LocalPosition.Z) < 0.5f;

    public static bool IsPaymentTarget(
        CastleObjectKey candidate,
        IEnumerable<CastleObjectPolicy> policies) =>
        policies.Any(policy =>
            policy.Cost.Enabled &&
            policy.Cost.PaymentTarget != null &&
            SameObject(candidate, policy.Cost.PaymentTarget));
}
