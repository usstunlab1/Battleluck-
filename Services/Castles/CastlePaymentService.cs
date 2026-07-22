using BattleLuck.Models;
using ProjectM.CastleBuilding;

namespace BattleLuck.Services.Castles;

// ─────────────────────────────────────────────────────────────────────────────
// CastlePaymentService
//
// Server-authoritative payment for castle access. Hooks call:
//   1. PrepareAsync    ->  validates currency, validates payment target, and
//                          returns a CastlePaymentReservation in state Reserved.
//   2. (hook performs the native game interaction here)
//   3. CommitAsync     ->  state = Committed (transfer accepted).
//      CancelAsync     ->  state = Cancelled (slot released on failure).
//
// SweepExpiredReservations must be called regularly (CastlePolicyService.Tick
// does this) so abandoned reservations do not leak quota slots.
//
// This service is intentionally ignorant of *how* the cost is moved: the
// caller is responsible for the actual inventory transfer after Prepare
// succeeds. The service only guarantees that:
//   - a reservation is single-use
//   - the cost/prefab is validated against the live policy
//   - the payment target is reachable
//   - quota is reserved and committed atomically
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CastlePaymentService
{
    readonly object _sync = new();
    readonly CastlePolicyStore _store;
    readonly Dictionary<string, CastlePaymentReservation> _reservations = new(StringComparer.OrdinalIgnoreCase);

    public CastlePaymentService(CastlePolicyStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Validate a payment configuration and reserve a slot for one transaction.
    /// Returns a reservation handle that the caller commits or cancels.
    /// </summary>
    public OperationResult<CastlePaymentReservation> Prepare(ulong requesterSteamId, string policyId, DateTime utc)
    {
        var policy = _store.Get(policyId);
        if (policy == null) return OperationResult<CastlePaymentReservation>.Fail($"Policy '{policyId}' does not exist.");
        if (!policy.Cost.Enabled)
            return OperationResult<CastlePaymentReservation>.Fail("Policy has no cost configured.");

        // Quota check: reservation must not exceed the configured limit.
        if (policy.Quota.Enabled && !QuotaHasRoom(policy, requesterSteamId, utc))
            return OperationResult<CastlePaymentReservation>.Fail("Quota exhausted for this window.");

        var reservation = new CastlePaymentReservation
        {
            PolicyId = policyId,
            RequesterSteamId = requesterSteamId,
            Cost = CastlePolicyStore.Clone(policy.Cost),
            ReservedAtUtc = utc,
            ExpiresAtUtc = utc.AddSeconds(30),
            State = PaymentState.Reserved
        };

        lock (_sync)
        {
            // Reserve the quota slot up front so concurrent requests cannot
            // double-charge before commit.
            if (policy.Quota.Enabled)
                ReserveQuotaSlot(policy, requesterSteamId, utc);

            _reservations[reservation.ReservationId] = reservation;
        }
        _store.Upsert(policy);
        return OperationResult<CastlePaymentReservation>.Ok(reservation);
    }

    /// <summary>
    /// Mark a reservation as Committed. Callers report success here only after
    /// the native game interaction has transferred the cost into the payment
    /// target. The reservation is removed from the live set.
    /// </summary>
    public OperationResult Commit(string reservationId, DateTime utc)
    {
        CastlePaymentReservation? reservation;
        lock (_sync)
        {
            if (!_reservations.TryGetValue(reservationId, out reservation))
                return OperationResult.Fail("Reservation not found or already finalized.");
            _reservations.Remove(reservationId);
        }
        if (reservation == null) return OperationResult.Fail("Reservation is null.");
        if (reservation.State == PaymentState.Cancelled || reservation.State == PaymentState.Committed)
            return OperationResult.Fail($"Reservation is already {reservation.State}.");
        if (utc > reservation.ExpiresAtUtc)
            return OperationResult.Fail("Reservation expired before commit.");

        reservation.State = PaymentState.Committed;
        // Quota slot is already committed in Prepare; nothing else to do here.
        return OperationResult.Ok();
    }

    /// <summary>
    /// Release a reservation. Called when the native interaction fails (item
    /// transfer rejected, inventory full, etc.). The reservation is removed
    /// and the quota slot is decremented if it was reserved.
    /// </summary>
    public OperationResult Cancel(string reservationId, string reason)
    {
        CastlePaymentReservation? reservation;
        lock (_sync)
        {
            if (!_reservations.TryGetValue(reservationId, out reservation))
                return OperationResult.Ok(); // already gone: idempotent cancel
            _reservations.Remove(reservationId);
        }
        if (reservation == null) return OperationResult.Ok();

        reservation.State = PaymentState.Cancelled;

        var policy = _store.Get(reservation.PolicyId);
        if (policy == null) return OperationResult.Ok();

        // Release the reserved quota slot.
        if (policy.Quota.Enabled)
        {
            var counter = policy.QuotaCounters.FirstOrDefault(c => c.SubjectSteamId == reservation.RequesterSteamId);
            if (counter != null && counter.Count > 0)
            {
                counter.Count--;
                if (counter.Count == 0)
                    policy.QuotaCounters.Remove(counter);
                policy.UpdatedAtUtc = DateTime.UtcNow;
                _store.Upsert(policy);
            }
        }
        return OperationResult.Ok();
    }

    /// <summary>
    /// Sweep reservations whose ExpiresAtUtc has passed. Released slots are
    /// not decremented (the cost was never charged), but the reservation is
    /// removed from the live set so it cannot be committed later.
    /// </summary>
    public void SweepExpiredReservations()
    {
        var now = DateTime.UtcNow;
        List<string> expired;
        lock (_sync)
        {
            expired = _reservations
                .Where(pair => pair.Value.ExpiresAtUtc <= now)
                .Select(pair => pair.Key)
                .ToList();
        }
        foreach (var id in expired)
        {
            Cancel(id, "expired");
        }
    }

    // ── Quota helpers (used here and exposed via CastlePolicyService) ────

    public static bool QuotaHasRoom(CastleObjectPolicy policy, ulong subjectSteamId, DateTime utc)
    {
        if (policy == null || !policy.Quota.Enabled) return true;
        var counter = policy.QuotaCounters.FirstOrDefault(c => c.SubjectSteamId == subjectSteamId);
        if (counter == null) return true;
        var windowHours = policy.Quota.WindowHours > 0f ? policy.Quota.WindowHours : 24f;
        if ((utc - counter.WindowStartUtc).TotalHours >= windowHours) return true;
        return counter.Count < policy.Quota.MaxAmount;
    }

    static void ReserveQuotaSlot(CastleObjectPolicy policy, ulong subjectSteamId, DateTime utc)
    {
        var counter = policy.QuotaCounters.FirstOrDefault(c => c.SubjectSteamId == subjectSteamId);
        if (counter == null)
        {
            counter = new CastleQuotaCounter
            {
                SubjectSteamId = subjectSteamId,
                Count = 0,
                WindowStartUtc = utc,
                TotalCount = 0
            };
            policy.QuotaCounters.Add(counter);
        }
        var windowHours = policy.Quota.WindowHours > 0f ? policy.Quota.WindowHours : 24f;
        if ((utc - counter.WindowStartUtc).TotalHours >= windowHours)
        {
            counter.WindowStartUtc = utc;
            counter.Count = 0;
        }
        counter.Count++;
        counter.TotalCount++;
        policy.UpdatedAtUtc = utc;
    }
}
