using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks frame-owned returns and retryable cleanup of bounded compiled assets.</summary>
public sealed class RetainedRenderingCacheTests
{
    /// <summary>Checks abandoned uploads are not published and completed assets are reused.</summary>
    [Fact]
    public void OnlyCompletedUploadsBecomeResident()
    {
        using var cache = new RetainedDrawingCache2D(new());
        RetainedDrawingCache2D.Key key = Key();
        var cold = new Batch();
        RetainedDrawingCache2D.FrameLease abandoned = cache.AcquireFrame();
        abandoned.Assets.Add(new(key, cold));
        cache.ReleaseFrame(abandoned, false);
        Assert.Equal(1, cold.Disposals);
        Assert.Null(cache.Take(key));
        var warm = new Batch();
        var asset = new RetainedDrawingCache2D.Asset(key, warm);
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        first.Assets.Add(asset);
        cache.ReleaseFrame(first, true);
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        Assert.Same(asset, cache.Take(key));
        second.Assets.Add(asset);
        Assert.True(asset.Resident);
        cache.ReleaseFrame(second, false);
        RetainedDrawingCache2D.FrameLease third = cache.AcquireFrame();
        Assert.Same(asset, cache.Take(key));
        third.Assets.Add(asset);
        cache.ReleaseFrame(third, true);
    }

    /// <summary>Checks idle budgets evict only returned assets, never an active frame.</summary>
    [Fact]
    public void ZeroBudgetEvictsOnlyAfterReturn()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 0 });
        var batch = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        Assert.Equal(0, batch.Disposals);
        cache.Dispose();
        Assert.Equal(0, batch.Disposals);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(1, batch.Disposals);
    }

    /// <summary>Checks order-independent keys include exact target and property values.</summary>
    [Fact]
    public void TargetAndPropertyChangesInvalidateOnlyTheirAssets()
    {
        RetainedDrawingCache2D.Key key = Key();
        Assert.True(key.Matches(Key()));
        Assert.False(key.Matches(key with { Width = 2048 }));
        Assert.False(key.Matches(key with { Format = TextureFormat.Rgba16Float }));
        Assert.False(key.Matches(key with { PixelScale = 2 }));
        Assert.False(key.Matches(key with { Transform = Matrix3x2.CreateTranslation(1, 0) }));
    }

    /// <summary>Checks failed eviction cannot reissue a partially disposed asset and can be retried.</summary>
    [Fact]
    public void FailedEvictionQuarantinesTheAssetUntilCleanupSucceeds()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 0 });
        var batch = new Batch { FailuresRemaining = 1 };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(frame, true));
        Assert.Null(cache.Take(Key()));
        Assert.Equal(0, cache.Statistics.CachedAssets);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(2, batch.Disposals);
    }

    /// <summary>Checks a failed old return cannot publish an asset owned by a newer frame.</summary>
    [Fact]
    public void OldReturnRetryCannotReturnAnAssetBorrowedAgain()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = 1 });
        var a = new Batch { FailuresRemaining = 1 };
        var b = new Batch();
        var assetB = new RetainedDrawingCache2D.Asset(Key() with { Width = 128 }, b);
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        first.Assets.Add(new(Key(), a));
        first.Assets.Add(assetB);
        Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(first, true));
        Assert.Equal(1, cache.Statistics.CachedAssets);
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        Assert.Same(assetB, cache.Take(assetB.Key));
        second.Assets.Add(assetB);
        cache.ReleaseFrame(first, true);
        Assert.Equal(1, cache.Statistics.ActiveFrames);
        Assert.Null(cache.Take(assetB.Key));
        RetainedDrawingCache2D.FrameLease third = cache.AcquireFrame();
        Assert.Null(cache.Take(assetB.Key));
        cache.ReleaseFrame(third, false);
        Assert.Equal(0, b.Disposals);
        Assert.Equal(1, cache.Statistics.ActiveFrames);
        cache.ReleaseFrame(second, true);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(1, cache.Statistics.CachedAssets);
    }

    /// <summary>Checks failed abandoned cleanup never consumes inaccessible frame slots.</summary>
    [Fact]
    public void AbandonedCleanupFailuresReturnSlotsAndRemainOwned()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxFramesInFlight = 1 });
        var batches = new List<Batch>();
        for (int index = 0; index < 3; index++)
        {
            RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
            var batch = new Batch { FailuresRemaining = 1 };
            batches.Add(batch);
            frame.Assets.Add(new(Key(), batch));
            Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(frame, false));
            Assert.Equal(0, cache.Statistics.ActiveFrames);
            Assert.Null(cache.Take(Key()));
        }

        RetainedDrawingCache2D.FrameLease next = cache.AcquireFrame();
        cache.ReleaseFrame(next, false);
        Assert.All(batches, batch => Assert.Equal(2, batch.Disposals));
        Assert.Equal(0, cache.Statistics.ActiveFrames);
    }

    /// <summary>Checks cleanup visits every failed resource and retries without caller-owned frames.</summary>
    [Fact]
    public void AbandonedCleanupAttemptsEveryAssetBeforeThrowing()
    {
        var cache = new RetainedDrawingCache2D(new());
        var first = new Batch { FailuresRemaining = 1 };
        var second = new Batch { FailuresRemaining = 1 };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), first));
        frame.Assets.Add(new(Key(), second));
        AggregateException failure = Assert.Throws<AggregateException>(() => cache.ReleaseFrame(frame, false));
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(2, first.Disposals);
        Assert.Equal(2, second.Disposals);
    }

    /// <summary>Checks a batch run retains each failed cleanup and does not stop at the first throw.</summary>
    [Fact]
    public void PartialBatchRunCleanupRetriesOnlyFailedBatches()
    {
        var first = new Batch { FailuresRemaining = 1 };
        var second = new Batch();
        var third = new Batch { FailuresRemaining = 1 };
        var run = new RetainedBatchRun2D([first, second, third], false);
        Assert.Throws<AggregateException>(() => run.Dispose());
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, third.Disposals);
        run.Dispose();
        run.Dispose();
        Assert.Equal(2, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(2, third.Disposals);
    }

    /// <summary>Active snapshots reserve cache bytes without being disposed or retroactively rejected.</summary>
    [Fact]
    public void ActiveSnapshotBytesEvictOnlyCompletedIdleOwners()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedBytes = 24 });
        var idle = new Batch();
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        first.Assets.Add(new(Key(), idle));
        cache.ReleaseFrame(first, true);
        var active = new Batch();
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        second.Assets.Add(new(Key() with { Width = 128 }, active));
        cache.AdmitFrame(second);
        Assert.Equal(1, idle.Disposals);
        Assert.Equal(0, active.Disposals);
        Assert.Equal(0, cache.Statistics.CachedAssets);
        cache.ReleaseFrame(second, false);
        Assert.Equal(1, active.Disposals);
    }

    /// <summary>One shared allocation is charged once across idle and active assets.</summary>
    [Fact]
    public void SharedOwnerChargesAreDeduplicatedAcrossSnapshots()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedBytes = 24 });
        object shared = new();
        var first = new Batch { SharedOwner = shared };
        var second = new Batch { SharedOwner = shared };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), first));
        var other = new RetainedDrawingCache2D.Asset(Key() with { Width = 128 }, second);
        frame.Assets.Add(other);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(2, cache.Statistics.CachedAssets);
        Assert.Equal(16, cache.Statistics.CachedBytes);
        RetainedDrawingCache2D.FrameLease active = cache.AcquireFrame();
        active.Assets.Add(cache.Take(other.Key)!);
        cache.AdmitFrame(active);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(0, second.Disposals);
        cache.ReleaseFrame(active, true);
        Assert.Equal(16, cache.Statistics.CachedBytes);
    }

    /// <summary>Repeated observations reuse scratch capacity without retaining allocation owners.</summary>
    [Fact]
    public void AccountingReusesEmptyStorageAndRereadsMutableContributions()
    {
        using var cache = new RetainedDrawingCache2D(new());
        var batch = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        cache.ReleaseFrame(frame, true);
        Dictionary<object, long> storage = batch.LastOwners!;
        int calls = batch.AccountCalls;
        Assert.Empty(storage);
        for (int index = 0; index < 5; index++)
        {
            batch.Bytes = 17 + index;
            Assert.Equal(batch.Bytes, cache.Statistics.CachedBytes);
            Assert.Same(storage, batch.LastOwners);
            Assert.Empty(storage);
        }

        Assert.Equal(calls + 5, batch.AccountCalls);
    }

    /// <summary>A failed traversal clears owners before the next independent observation.</summary>
    [Fact]
    public void FailedAccountingDoesNotLeakOwnersIntoLaterReads()
    {
        using var cache = new RetainedDrawingCache2D(new());
        var batch = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        cache.ReleaseFrame(frame, true);
        Dictionary<object, long> storage = batch.LastOwners!;
        batch.AfterAccount = () => throw new InvalidOperationException("Injected estimate failure.");
        Assert.Throws<InvalidOperationException>(() => cache.Statistics);
        Assert.Empty(storage);
        batch.AfterAccount = null;
        batch.Owner = new();
        batch.Bytes = 37;
        Assert.Equal(37, cache.Statistics.CachedBytes);
        Assert.Same(storage, batch.LastOwners);
        Assert.Empty(storage);
    }

    /// <summary>Recursive observations borrow independent scratch storage from their outer reader.</summary>
    [Fact]
    public void ReentrantAccountingDoesNotClearTheOuterOwnerSet()
    {
        using var cache = new RetainedDrawingCache2D(new());
        var first = new Batch();
        var second = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), first));
        frame.Assets.Add(new(Key(), second));
        cache.ReleaseFrame(frame, true);
        bool entered = false;
        var observed = new List<Dictionary<object, long>>();
        first.AfterAccount = () =>
        {
            if (entered)
            {
                return;
            }

            entered = true;
            Dictionary<object, long> outer = first.LastOwners!;
            observed.Add(outer);
            for (int index = 0; index < 2; index++)
            {
                Assert.Equal(32, cache.Statistics.CachedBytes);
                Assert.NotSame(outer, first.LastOwners);
                observed.Add(first.LastOwners!);
                Assert.Single(outer);
            }

            second.AfterAccount = () => throw new InvalidOperationException("Nested estimate failure.");
            Assert.Throws<InvalidOperationException>(() => cache.Statistics);
            observed.Add(second.LastOwners!);
            second.AfterAccount = null;
            Assert.Single(outer);
        };
        Assert.Equal(32, cache.Statistics.CachedBytes);
        Assert.True(entered);
        Assert.All(observed, owners => Assert.Empty(owners));
    }

    /// <summary>Mutable owner identities and saturated sums match independent full recomputation.</summary>
    /// <param name="budget">The exact retained-byte admission budget.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(long.MaxValue)]
    public void AccountingMatchesFreshDictionaryAcrossDeterministicTransitions(long budget)
    {
        var random = new Random(1979);
        object[] owners = [new(), new(), new(), new()];
        for (int cycle = 0; cycle < 40; cycle++)
        {
            using var cache = new RetainedDrawingCache2D(new() { MaxCachedBytes = budget });
            Batch[] batches = Enumerable.Range(0, 7).Select(_ => new Batch
            {
                Owner = owners[random.Next(owners.Length)],
                Bytes = random.Next(3) == 0 ? long.MaxValue : random.Next(1, 100),
            }).ToArray();
            RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
            foreach (Batch batch in batches)
            {
                frame.Assets.Add(new(Key(), batch));
            }

            cache.ReleaseFrame(frame, true);
            for (int change = 0; change < 3; change++)
            {
                foreach (Batch batch in batches)
                {
                    batch.Owner = owners[random.Next(owners.Length)];
                    batch.Bytes = random.Next(2) == 0 ? long.MaxValue : random.Next(1, 100);
                }

                var reference = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
                foreach (Batch batch in batches.Where(batch => batch.Disposals == 0))
                {
                    reference.TryAdd(batch.Owner!, batch.Bytes);
                }

                long expected = 0;
                foreach (long count in reference.Values)
                {
                    expected = count > long.MaxValue - expected ? long.MaxValue : expected + count;
                }

                Assert.Equal(expected, cache.Statistics.CachedBytes);
            }

            // Drain this generation through ordinary take/abandon transitions.
            RetainedDrawingCache2D.FrameLease drain = cache.AcquireFrame();
            while (cache.Take(Key()) is { } asset)
            {
                drain.Assets.Add(asset);
            }

            cache.ReleaseFrame(drain, false);

            // Previously completed assets remain resident after abandonment.
            cache.Dispose();
            Assert.All(batches, batch => Assert.Equal(1, batch.Disposals));
        }
    }

    /// <summary>Shared pages are charged once and active snapshots reserve the exact boundary.</summary>
    [Fact]
    public void SharedPagesAndMixedActiveOwnersPreserveExactBudget()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedBytes = 288 });
        var page = new DrawList2D([]);
        RetainedDrawingCache2D.Key key = Key() with { Pages = [page] };
        var first = new Batch();
        var second = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(key, first));
        frame.Assets.Add(new(key with { Width = 128 }, second));
        cache.ReleaseFrame(frame, true);
        Assert.Equal(288, cache.Statistics.CachedBytes);
        Assert.Equal(0, first.Disposals);
        RetainedDrawingCache2D.FrameLease active = cache.AcquireFrame();
        var third = new Batch();
        active.Assets.Add(new(key with { Width = 256 }, third));
        Assert.Equal(288, cache.Statistics.CachedBytes);
        cache.AdmitFrame(active);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, second.Disposals);
        Assert.Equal(0, third.Disposals);
        Assert.Equal(272, cache.Statistics.CachedBytes);
        cache.ReleaseFrame(active, true);
        Assert.Equal(288, cache.Statistics.CachedBytes);
    }

    /// <summary>Stable owners are collected once, not walked by subsequent observations or reuse.</summary>
    [Fact]
    public void StableAccountingDoesNotRevisitUnchangedAssets()
    {
        using var cache = new RetainedDrawingCache2D(new());
        Batch[] batches = Enumerable.Range(0, 26).Select(_ => new Batch { HasStableOwners = true }).ToArray();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        foreach (Batch batch in batches)
        {
            frame.Assets.Add(new(Key(), batch));
        }

        cache.ReleaseFrame(frame, true);
        Assert.All(batches, batch => Assert.Equal(1, batch.AccountCalls));
        for (int read = 0; read < 5; read++)
        {
            Assert.Equal(26 * 16, cache.Statistics.CachedBytes);
        }

        RetainedDrawingCache2D.FrameLease reuse = cache.AcquireFrame();
        while (cache.Take(Key()) is { } asset)
        {
            reuse.Assets.Add(asset);
        }

        cache.AdmitFrame(reuse);
        cache.ReleaseFrame(reuse, true);
        Assert.All(batches, batch => Assert.Equal(1, batch.AccountCalls));
    }

    /// <summary>Duplicate page identities are deduplicated within and across asset snapshots.</summary>
    [Fact]
    public void StableAccountingKeepsSharedPagesUntilLastScopeLeaves()
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedBytes = 288 });
        var page = new DrawList2D([]);
        var first = new Batch { HasStableOwners = true };
        var second = new Batch { HasStableOwners = true };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        RetainedDrawingCache2D.Key key = Key() with { Pages = [page, page] };
        frame.Assets.Add(new(key, first));
        var asset = new RetainedDrawingCache2D.Asset(key with { Width = 128 }, second);
        frame.Assets.Add(asset);
        cache.ReleaseFrame(frame, true);
        Assert.Equal(288, cache.Statistics.CachedBytes);
        RetainedDrawingCache2D.FrameLease active = cache.AcquireFrame();
        active.Assets.Add(cache.Take(asset.Key)!);
        Assert.Equal(272, cache.Statistics.CachedBytes);
        cache.AdmitFrame(active);
        Assert.Equal(0, first.Disposals);
        cache.ReleaseFrame(active, true);
        Assert.Equal(288, cache.Statistics.CachedBytes);
        cache.Dispose();
        Assert.Equal(0, cache.Statistics.CachedBytes);
        Assert.All(new[] { first, second }, batch => Assert.Equal(1, batch.Disposals));
    }

    /// <summary>A saturated aggregate recovers its exact remaining value after a large owner leaves.</summary>
    [Fact]
    public void LedgerSaturationIsReversibleAndPoolReuseIsCleared()
    {
        var ledger = new RetainedOwnerLedger2D();
        KeyValuePair<object, long>[] large = [new(new object(), long.MaxValue)];
        KeyValuePair<object, long>[] small = [new(new object(), 37)];
        ledger.Attach(large, idle: true);
        ledger.Attach(small, idle: false);
        Assert.Equal(long.MaxValue, ledger.Bytes(includeActive: true));
        Assert.Equal(long.MaxValue, ledger.Bytes(includeActive: false));
        ledger.Detach(large, idle: true);
        Assert.Equal(37, ledger.Bytes(includeActive: true));
        Assert.Equal(0, ledger.Bytes(includeActive: false));
        ledger.Detach(small, idle: false);
        Assert.Equal(0, ledger.OwnerCount);
        ledger.Attach(small, idle: true);
        Assert.Equal(37, ledger.Bytes(includeActive: false));
        ledger.Detach(small, idle: true);
        Assert.Equal(0, ledger.Bytes(includeActive: true));
        Assert.Equal(0, ledger.OwnerCount);
    }

    /// <summary>Seeded reference transitions match a fresh union in both accounting scopes.</summary>
    [Fact]
    public void LedgerMatchesReferenceAcrossRandomScopeTransitions()
    {
        var random = new Random(20261116);
        object[] owners = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
        var ledger = new RetainedOwnerLedger2D();
        var attached = new List<(KeyValuePair<object, long>[] Owners, bool Idle)>();
        for (int step = 0; step < 1000; step++)
        {
            if (attached.Count == 0 || random.Next(2) == 0)
            {
                IEnumerable<int> ids = Enumerable.Range(0, random.Next(1, 8)).Select(_ => random.Next(owners.Length)).Distinct();
                KeyValuePair<object, long>[] snapshot = ids.Select(id => new KeyValuePair<object, long>(owners[id], id == 0 ? long.MaxValue : id)).ToArray();
                bool idle = random.Next(2) == 0;
                ledger.Attach(snapshot, idle);
                attached.Add((snapshot, idle));
            }
            else
            {
                int index = random.Next(attached.Count);
                (KeyValuePair<object, long>[] Owners, bool Idle) item = attached[index];
                ledger.Detach(item.Owners, item.Idle);
                attached.RemoveAt(index);
            }

            foreach (bool active in new[] { false, true })
            {
                var union = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
                foreach ((KeyValuePair<object, long>[] Owners, bool Idle) item in attached.Where(item => active || item.Idle))
                {
                    foreach (KeyValuePair<object, long> owner in item.Owners)
                    {
                        union.TryAdd(owner.Key, owner.Value);
                    }
                }

                Int128 expected = union.Values.Aggregate((Int128)0, (sum, bytes) => sum + bytes);
                Assert.Equal(expected > long.MaxValue ? long.MaxValue : (long)expected, ledger.Bytes(active));
            }
        }

        foreach ((KeyValuePair<object, long>[] Owners, bool Idle) item in attached)
        {
            ledger.Detach(item.Owners, item.Idle);
        }

        Assert.Equal(0, ledger.OwnerCount);
        Assert.Equal(0, ledger.Bytes(true));
    }

    /// <summary>One shared live plan updates both scope totals without recollecting fixed owners.</summary>
    /// <returns>The asynchronous plan finalization.</returns>
    [Fact]
    public async Task LedgerRevalidatesChangingSharedPlanAndRejectsWrongScopeDetachAsync()
    {
        var device = new ProfilingDevice();
        using var sampler = new DispatchSampler();
        using var plan = new DrawingArgumentPlan2D(device);
        plan.RegisterSampler(sampler, _ => { });
        var ledger = new RetainedOwnerLedger2D();
        KeyValuePair<object, long>[] snapshot = [new(plan, plan.EstimatedBytes)];
        ledger.Attach(snapshot, idle: true);
        ledger.Attach(snapshot, idle: false);
        Assert.Equal(256, ledger.Bytes(true));
        await plan.SealAsync();
        Assert.True(plan.MemorySizeIsStable);
        Assert.Equal(512, ledger.Bytes(false));
        ledger.Detach(snapshot, idle: true);
        Assert.Throws<InvalidOperationException>(() => ledger.Detach(snapshot, idle: true));
        Assert.Equal(512, ledger.Bytes(true));
        Assert.Equal(0, ledger.Bytes(false));
        ledger.Detach(snapshot, idle: false);
        Assert.Equal(0, ledger.OwnerCount);
    }

    /// <summary>Accounting failure must not insert an asset or repeat already completed frame returns.</summary>
    [Fact]
    public void FailedStableAssetInsertionIsTransactional()
    {
        using var cache = new RetainedDrawingCache2D(new());
        var good = new Batch { HasStableOwners = true };
        var bad = new Batch { HasStableOwners = true, AfterAccount = () => throw new InvalidOperationException("Injected accounting failure.") };
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), good));
        Assert.Throws<InvalidOperationException>(() => frame.Assets.Add(new(Key(), bad)));
        Assert.Single(frame.Assets);
        Assert.Empty(bad.LastOwners!);
        cache.ReleaseFrame(frame, true);
        bad.AfterAccount = null;
        cache.ReleaseFrame(frame, true);
        Assert.Equal(1, cache.Statistics.CachedAssets);
        Assert.Equal(16, cache.Statistics.CachedBytes);
        cache.Dispose();
        Assert.Equal(1, good.Disposals);
        Assert.Equal(0, bad.Disposals);
        bad.Dispose();
    }

    /// <summary>A reentrant frame release during accounting cannot leave an attached unpublished owner.</summary>
    [Fact]
    public void ReentrantReleaseDuringStableInsertionRollsBack()
    {
        using var cache = new RetainedDrawingCache2D(new());
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        var batch = new Batch { HasStableOwners = true, AfterAccount = () => cache.ReleaseFrame(frame, false) };
        Assert.Throws<InvalidOperationException>(() => frame.Assets.Add(new(Key(), batch)));
        Assert.Empty(frame.Assets);
        Assert.Equal(0, cache.Statistics.CachedAssets);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(0, cache.Statistics.CachedBytes);
        batch.Dispose();
    }

    /// <summary>Real shared registrations, packing release and pool returns retain the old full-union charge.</summary>
    /// <returns>The asynchronous preparation and upload completion.</returns>
    [Fact]
    public async Task BuiltinLedgerMatchesFreshOwnerUnionThroughPackingAndPoolReturnAsync()
    {
        var device = new ProfilingDevice();
        using var state = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        using var cache = new RetainedDrawingCache2D(new());
        cache.BindStorageOwner(state);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var context = new RenderPreparationContext2D(device, target, state, plan);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 2, 3, 4), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
        DrawList2D page = canvas.End();
        IPreparedCommandBatch2D first = await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, page.Commands.ToArray(), default);
        IPreparedCommandBatch2D second = await PreparedRecordBatch2D.PrepareAsync<DrawingCommand2D>(context, page.Commands.ToArray(), default);
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        RetainedDrawingCache2D.Key key = Key() with { Pages = [page, page] };
        frame.Assets.Add(new(key, first));
        frame.Assets.Add(new(key with { Width = 128 }, second));
        cache.AdmitFrame(frame);
        Assert.Equal(0, cache.Statistics.CachedBytes);
        await plan.SealAsync();
        Assert.True(plan.MemorySizeIsStable);
        cache.ReleaseFrame(frame, true);
        var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
        ((IRetainedMemory2D)first).AccountMemory(owners);
        ((IRetainedMemory2D)second).AccountMemory(owners);
        owners.TryAdd(page, 256L + ((long)page.Commands.Count * 256));
        long expected = owners.Values.Sum() + state.IdleRecordBytes;
        Assert.Equal(expected, cache.Statistics.CachedBytes);
        Assert.Equal(expected, cache.Statistics.CachedBytes);
        cache.Dispose();
        Assert.Equal(state.IdleRecordBytes, cache.Statistics.CachedBytes);
    }

    /// <summary>Traversal capacity is released without another observation at ownership boundaries.</summary>
    /// <param name="transition">The transition that releases the last accounted asset.</param>
    [Theory]
    [InlineData("abandon")]
    [InlineData("evict")]
    [InlineData("take")]
    [InlineData("dispose-idle")]
    [InlineData("dispose-active")]
    [InlineData("cleanup-failure")]
    public void AccountingScratchIsReleasedAtOwnershipBoundary(string transition)
    {
        using var cache = new RetainedDrawingCache2D(new() { MaxCachedAssets = transition == "evict" ? 0 : 64 });
        var batch = new Batch();
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        frame.Assets.Add(new(Key(), batch));
        cache.AdmitFrame(frame);
        Dictionary<object, long> scratch = Assert.IsType<Dictionary<object, long>>(batch.LastOwners);
        Assert.True(scratch.EnsureCapacity(0) > 0);
        Assert.Empty(scratch);
        System.Reflection.FieldInfo field = typeof(RetainedDrawingCache2D).GetField("_memoryOwners", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Same(scratch, field.GetValue(cache));

        if (transition == "dispose-active")
        {
            cache.Dispose();
            Assert.Equal(0, batch.Disposals);
        }
        else if (transition == "dispose-idle" || transition == "take")
        {
            cache.ReleaseFrame(frame, true);
            Assert.Same(scratch, field.GetValue(cache));
            if (transition == "take")
            {
                RetainedDrawingCache2D.FrameLease next = cache.AcquireFrame();
                RetainedDrawingCache2D.Asset asset = Assert.IsType<RetainedDrawingCache2D.Asset>(cache.Take(Key()));
                Assert.Null(field.GetValue(cache));
                next.Assets.Add(asset);
                cache.ReleaseFrame(next, true);
            }

            cache.Dispose();
        }
        else if (transition == "cleanup-failure")
        {
            batch.FailuresRemaining = 1;
            Assert.Throws<InvalidOperationException>(() => cache.ReleaseFrame(frame, false));
            Assert.Null(field.GetValue(cache));
            cache.RetryCleanup();
        }
        else
        {
            cache.ReleaseFrame(frame, transition == "evict");
        }

        Assert.Null(field.GetValue(cache));
        if (transition == "dispose-active")
        {
            cache.ReleaseFrame(frame, true);
        }

        Assert.Equal(transition == "cleanup-failure" ? 2 : 1, batch.Disposals);
        Assert.Equal(0, cache.Statistics.ActiveFrames);
        Assert.Equal(0, cache.Statistics.CachedBytes);
    }

    private static RetainedDrawingCache2D.Key Key() => new([], Matrix3x2.Identity, [], 64, 64, TextureFormat.Rgba8Srgb, 1);

    private sealed class Batch : IRetainedCommandBatch2D, IRetainedMemory2D
    {
        public bool HasStableOwners { get; init; }

        public long EstimatedBytes => Bytes;

        internal long Bytes { get; set; } = 16;

        internal object? Owner { get; set; }

        internal Dictionary<object, long>? LastOwners { get; private set; }

        internal int AccountCalls { get; private set; }

        internal Action? AfterAccount { get; set; }

        internal int Disposals { get; private set; }

        internal object? SharedOwner { get; init; }

        internal int FailuresRemaining { get; set; }

        public void AccountMemory(Dictionary<object, long> owners)
        {
            LastOwners = owners;
            AccountCalls++;
            owners.TryAdd(Owner ?? SharedOwner ?? this, EstimatedBytes);
            AfterAccount?.Invoke();
        }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
        {
        }

        public void Dispose()
        {
            Disposals++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Injected resource disposal failure.");
            }
        }
    }
}
