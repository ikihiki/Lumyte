namespace Lumyte.Graphics.Native.Resources.Tests.Unit.Packages;

public sealed class GpuPackageTests
{
    private static GpuResourceManager Create(TestResourceBackend backend) => new(backend, new(1024, 8, 8));
    private static NativeGpuTextureDescription TextureDescription => new(NativeGpuTextureDimension.TwoD,
        2, 2, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination | NativeGpuTextureUsage.Sampled);
    private static NativeGpuTextureCopyFootprint Footprint => new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(2, 2, 1), 16, 32);
    private static byte[] Pixels()
    {
        byte[] bytes = new byte[24];
        for (int index = 0; index < 8; index++) { bytes[index] = (byte)(index + 1); bytes[index + 16] = (byte)(index + 9); }
        return bytes;
    }

    [Theory]
    [InlineData(GpuPackagePlacement.Pools)]
    [InlineData(GpuPackagePlacement.SingleAllocation)]
    public async Task ImportCopiesPlanOwnedBytesIntoTheExportedBuffer(GpuPackagePlacement placement)
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        byte[] bytes = [1, 2, 3, 4]; GpuPackagePlan plan = new([new GpuPackageBuffer("buffer", new(4), bytes)], [new("Buffer", "buffer")]);
        Array.Fill(bytes, (byte)99);

        GpuPackageRef package = await scope.ImportPackageAsync(plan, placement);
        byte[] result = await manager.ReadBufferAsync(package.GetExport<GpuBufferRef>("Buffer"));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public async Task CpuVisibleInitialDataUsesItsMappingWithoutGpuCopy()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuPackagePlan plan = new([new GpuPackageBuffer("buffer", new(4, NativeGpuMemoryKind.CpuVisible), [1, 3, 5, 7])], [new("Buffer", "buffer")]);

        GpuPackageRef package = await scope.ImportPackageAsync(plan);

        Assert.Equal(new byte[] { 1, 3, 5, 7 }, TestResourceBackend.Read(manager.GetBufferRange(package.GetExport<GpuBufferRef>("Buffer"))));
        Assert.Equal(0, backend.Queue.SubmitCount);
        Assert.DoesNotContain("CopyBuffer", backend.Commands);
    }

    [Fact]
    public async Task OnePinnedExportKeepsEveryPlacedResourceInItsAllocationGroup()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuPackagePlan plan = new([new GpuPackageBuffer("one", new(32)), new GpuPackageBuffer("two", new(32))], [new("One", "one"), new("Two", "two")]);
        GpuPackageRef package = await scope.ImportPackageAsync(plan, GpuPackagePlacement.SingleAllocation);
        using GpuResourcePin pin = manager.Pin(package.GetExport("One")); scope.Release(package);

        manager.Collect();
        Assert.Empty(backend.Destroyed);
        Assert.Same(backend.Regions[0].Heap, backend.Regions[1].Heap);
        pin.Dispose(); manager.Collect();

        Assert.Equal(2, backend.Destroyed.OfType<TestResourceBackend.Region>().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextureUploadsUseTheBackendLayoutModelAndRespectCpuRows(bool explicitTransitions)
    {
        using TestResourceBackend backend = new() { ExplicitTransitions = explicitTransitions };
        await using GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuPackagePlan plan = new([new GpuPackageTexture("image", TextureDescription, [new GpuTextureUpload(Pixels(), Footprint, 2, 8)])], [new("Image", "image")]);

        GpuPackageRef package = await scope.ImportPackageAsync(plan);
        GpuTextureRef texture = package.GetExport<GpuTextureRef>("Image");
        byte[] readback = await manager.ReadTextureAsync(texture, Footprint, 2, 8, GpuTextureLayout.General, GpuTextureLayout.General);

        Assert.Equal(Enumerable.Range(1, 16).Select(value => (byte)value), Assert.Single(backend.Textures).Data);
        Assert.Equal(Pixels().AsSpan(0, 8).ToArray(), readback.AsSpan(0, 8).ToArray());
        Assert.Equal(Pixels().AsSpan(16, 8).ToArray(), readback.AsSpan(16, 8).ToArray());
        Assert.Contains(explicitTransitions ? "Discard:CopyDestination" : "Discard:General", backend.Commands);
        if (!explicitTransitions) { Assert.DoesNotContain(backend.Commands, command => command.StartsWith("Transition:", StringComparison.Ordinal)); }
    }

    [Fact]
    public async Task CancelledImportKeepsSubmittedStagingAndDestinationUntilCompletion()
    {
        using TestResourceBackend backend = new() { AutoComplete = false }; await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); using CancellationTokenSource cancellation = new();
        GpuPackagePlan plan = new([new GpuPackageBuffer("buffer", new(4), [1, 2, 3, 4])], [new("Buffer", "buffer")]);
        Task<GpuPackageRef> import = scope.ImportPackageAsync(plan, cancellation.Token);
        Assert.Equal(1, backend.Queue.SubmitCount);

        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => import);
        Assert.Empty(backend.Destroyed);
        backend.CompleteAll(); await manager.WaitIdleAsync();

        Assert.Equal(2, backend.Destroyed.OfType<TestResourceBackend.Region>().Count());
        Assert.Equal(0, manager.Statistics.ResourceCount);
    }

    [Fact]
    public async Task ImportPreservesBothPreparationAndCleanupFailures()
    {
        NativeGpuException primary = new("Timeline creation failed before submission.", -1);
        InvalidOperationException cleanup = new("Resource destruction failed.");
        using TestResourceBackend backend = new() { SemaphoreCreationError = primary, DestructionError = resource => resource is TestResourceBackend.Region ? cleanup : null };
        GpuResourceManager manager = Create(backend); using GpuResourceScope scope = manager.CreateScope();
        GpuPackagePlan plan = new([new GpuPackageBuffer("buffer", new(4), [1, 2, 3, 4])], [new("Buffer", "buffer")]);

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => scope.ImportPackageAsync(plan));

        Assert.Contains(primary, error.Flatten().InnerExceptions);
        Assert.Contains(cleanup, error.Flatten().InnerExceptions);
    }

    [Fact]
    public async Task ReclaimingADefaultViewRemovesItsCacheEntry()
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope();
        GpuTextureViewDescription view = new(NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
        GpuTextureRef texture = scope.CreateTexture(TextureDescription, defaultView: view);
        Assert.Equal(1, manager.Statistics.ViewCacheCount);

        scope.Release(texture); manager.Collect();

        Assert.Equal(0, manager.Statistics.ViewCacheCount);
        Assert.Equal(0u, manager.Statistics.ResourceDescriptorCount);
    }

    [Theory]
    [InlineData(NativeGpuMemoryKind.GpuOnly)]
    [InlineData(NativeGpuMemoryKind.CpuVisible)]
    public async Task StandaloneUploadUpdatesOnlyTheRequestedBytes(NativeGpuMemoryKind kind)
    {
        using TestResourceBackend backend = new(); await using GpuResourceManager manager = Create(backend);
        using GpuResourceScope scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(new(8, kind));
        await manager.UploadBufferAsync(buffer, new byte[8]);

        await manager.UploadBufferAsync(buffer, new byte[] { 3, 5, 7 }, 2);
        byte[] result = await manager.ReadBufferAsync(buffer);

        Assert.Equal(new byte[] { 0, 0, 3, 5, 7, 0, 0, 0 }, result);
    }

    [Fact]
    public void PackageDependenciesRejectCyclesBeforeGpuAllocation()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new GpuPackagePlan([
            new GpuPackageBuffer("a", new(4), dependencies: ["b"]),
            new GpuPackageBuffer("b", new(4), dependencies: ["a"])
        ], [new("A", "a")]));

        Assert.Equal("resources", error.ParamName);
    }
}
