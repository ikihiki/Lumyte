namespace Lumyte.Memory;

internal sealed class MemoryPoolState(MemoryPoolId id, MemoryBudget budget)
{
    private Ledger _ledger = new(0, 0, 0, 0, false);

    internal MemoryPoolId Id { get; } = id;

    internal SemaphoreSlim ReclaimSerial { get; } = new(1, 1);

    internal ulong ReleasedBytes => Volatile.Read(ref _ledger).ReleasedBytes;

    internal void Reserve(ulong bytes)
    {
        while (true)
        {
            Ledger before = Volatile.Read(ref _ledger);
            CheckCanRent(before);
            if (!Fits(before.CommittedBytes, before.ReservedBytes, bytes, budget.HardLimitBytes))
            {
                throw new MemoryBudgetExceededException(Id, bytes, Snapshot(before));
            }

            Ledger after = before with { ReservedBytes = checked(before.ReservedBytes + bytes) };
            if (TryUpdate(before, after))
            {
                return;
            }
        }
    }

    internal void Commit(ulong reservedBytes, MemoryUsage usage, bool enforceHardLimit)
    {
        while (true)
        {
            Ledger before = Volatile.Read(ref _ledger);
            ulong remainingReserved = checked(before.ReservedBytes - reservedBytes);
            if (enforceHardLimit && !Fits(before.CommittedBytes, remainingReserved, usage.Bytes, budget.HardLimitBytes))
            {
                throw new MemoryBudgetExceededException(Id, usage.Bytes, Snapshot(before));
            }

            Ledger after = before with
            {
                ReservedBytes = remainingReserved,
                CommittedBytes = checked(before.CommittedBytes + usage.Bytes),
                EstimatedBytes = usage.Accuracy == MemoryAccuracy.Estimate ? checked(before.EstimatedBytes + usage.Bytes) : before.EstimatedBytes,
            };
            if (TryUpdate(before, after))
            {
                return;
            }
        }
    }

    internal void ReleaseReservation(ulong bytes)
    {
        while (true)
        {
            Ledger before = Volatile.Read(ref _ledger);
            Ledger after = before with { ReservedBytes = checked(before.ReservedBytes - bytes) };
            if (TryUpdate(before, after))
            {
                return;
            }
        }
    }

    internal void ReleaseCharge(MemoryUsage usage)
    {
        while (true)
        {
            Ledger before = Volatile.Read(ref _ledger);
            Ledger after = before with
            {
                CommittedBytes = checked(before.CommittedBytes - usage.Bytes),
                EstimatedBytes = usage.Accuracy == MemoryAccuracy.Estimate ? checked(before.EstimatedBytes - usage.Bytes) : before.EstimatedBytes,
                ReleasedBytes = unchecked(before.ReleasedBytes + usage.Bytes),
            };
            if (TryUpdate(before, after))
            {
                return;
            }
        }
    }

    internal MemorySnapshot GetSnapshot() => Snapshot(Volatile.Read(ref _ledger));

    internal void CheckCanRent() => CheckCanRent(Volatile.Read(ref _ledger));

    internal void SetReclaiming(bool value)
    {
        while (true)
        {
            Ledger before = Volatile.Read(ref _ledger);
            Ledger after = before with { IsReclaiming = value };
            if (TryUpdate(before, after))
            {
                return;
            }
        }
    }

    private static bool Fits(ulong committed, ulong reserved, ulong requested, ulong hardLimit)
        => committed <= hardLimit && reserved <= hardLimit - committed && requested <= hardLimit - committed - reserved;

    private bool TryUpdate(Ledger before, Ledger after)
        => ReferenceEquals(Interlocked.CompareExchange(ref _ledger, after, before), before);

    private void CheckCanRent(Ledger ledger)
    {
        if (ledger.IsReclaiming)
        {
            throw new InvalidOperationException($"Memory pool '{Id}' cannot be reserved from a reclaimer callback.");
        }
    }

    private MemorySnapshot Snapshot(Ledger ledger) => new()
    {
        CommittedBytes = ledger.CommittedBytes,
        ReleasedBytes = ledger.ReleasedBytes,
        ReservedBytes = ledger.ReservedBytes,
        EstimatedBytes = ledger.EstimatedBytes,
        SoftLimitBytes = budget.SoftLimitBytes,
        HardLimitBytes = budget.HardLimitBytes,
        IsOverBudget = !Fits(ledger.CommittedBytes, ledger.ReservedBytes, 0, budget.HardLimitBytes),
    };

    private sealed record Ledger(ulong CommittedBytes, ulong ReservedBytes, ulong EstimatedBytes, ulong ReleasedBytes, bool IsReclaiming);
}
