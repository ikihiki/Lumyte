namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

public sealed class GpuResourceTransferTests
{
    private static readonly GpuBufferDescription Description = new(16, GpuBufferUsage.CopySource | GpuBufferUsage.CopyDestination | GpuBufferUsage.Storage);

    [Fact]
    public async Task UploadAndReadbackPreserveBytesOutsideTheSelectedRange()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(Description);
        await manager.UploadBufferAsync(buffer, new byte[] { 1, 2, 3, 4 }, 4);

        byte[] actual = await manager.ReadBufferAsync(buffer);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0 }, actual);
        Assert.Equal("Unmap", backend.Events[0]);
    }

    [Fact]
    public async Task MapWriteUploadUsesMappingWithoutAnIllegalCopyDestination()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        var description = new GpuBufferDescription(16, GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource);
        var plan = new GpuPackagePlan([new GpuPackageBuffer("upload", description, [4, 3, 2, 1], 4)], [new("Upload", "upload")]);

        GpuPackageRef package = await scope.ImportPackageAsync(plan);
        GpuBufferRef buffer = package.GetExport<GpuBufferRef>("Upload");

        Assert.Empty(backend.TestQueue.Submitted);
        Assert.Equal(description, buffer.Description);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, ((ManagerTestBackend.Buffer)manager.GetBufferRange(buffer).Buffer).Bytes[4..8]);
    }

    [Fact]
    public async Task TextureTransfersRespectPreparedRowPitch()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        GpuTextureRef texture = scope.CreateTexture(GpuBindingCacheTests.TextureDescription);
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(2, 2, 1), 256, 512);
        byte[] bytes = new byte[264]; bytes.AsSpan(0, 8).Fill(13); bytes.AsSpan(256, 8).Fill(37);

        await manager.UploadTextureAsync(texture, new(bytes, footprint));
        byte[] actual = await manager.ReadTextureAsync(texture, footprint);

        Assert.Equal(264, actual.Length);
        Assert.Equal(bytes.AsSpan(0, 8).ToArray(), actual.AsSpan(0, 8).ToArray());
        Assert.Equal(bytes.AsSpan(256, 8).ToArray(), actual.AsSpan(256, 8).ToArray());
    }

    [Fact]
    public async Task CancelledUploadKeepsStagingAndDestinationUntilGpuEnd()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend); var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);
        using var cancellation = new CancellationTokenSource();

        Task uploading = manager.UploadBufferAsync(buffer, new byte[] { 1, 2, 3, 4 }, cancellationToken: cancellation.Token).AsTask();
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploading);
        scope.Dispose(); manager.Trim();

        Assert.Equal(2, manager.Statistics.BufferCount);
        Assert.Empty(backend.Destroyed);
        backend.TestQueue.Complete(0); await manager.WaitIdleAsync(); manager.Trim();
        Assert.Equal(2, backend.Destroyed.Count);
    }

    [Fact]
    public async Task ReadbackDoesNotPublishBytesFromFailedExecution()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope(); GpuBufferRef buffer = scope.CreateBuffer(Description);

        Task<byte[]> reading = manager.ReadBufferAsync(buffer).AsTask();
        backend.TestQueue.Complete(0, new GpuDiagnostic(GpuDiagnosticKind.Validation, "copy failed"));
        GpuExecutionException error = await Assert.ThrowsAsync<GpuExecutionException>(() => reading);
        manager.Collect();

        Assert.Equal("copy failed", Assert.Single(error.Diagnostics).Message);
        Assert.DoesNotContain("Unmap", backend.Events);
    }

    [Fact]
    public async Task PackageOwnsPreparedBytesAndExportsTypedResources()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        byte[] bytes = [9, 8, 7, 6];
        var item = new GpuPackageBuffer("mesh", Description, bytes, 4);
        var plan = new GpuPackagePlan([item], [new("Mesh", "mesh")]); bytes.AsSpan().Fill(0);

        GpuPackageRef package = await scope.ImportPackageAsync(plan);
        byte[] actual = await manager.ReadBufferAsync(package.GetExport<GpuBufferRef>("Mesh"), 4, 4);

        Assert.Equal(new byte[] { 9, 8, 7, 6 }, actual);
        Assert.Throws<InvalidOperationException>(() => package.GetExport<GpuTextureRef>("Mesh"));
    }

    [Fact]
    public async Task ExportPinRetainsOnlyItsExplicitDependencies()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        var plan = new GpuPackagePlan([
            new GpuPackageBuffer("mesh", Description, dependencies: ["material"]),
            new GpuPackageBuffer("material", Description with { Size = 32 }),
            new GpuPackageBuffer("unrelated", Description with { Size = 64 })],
            [new("Mesh", "mesh"), new("Other", "unrelated")]);
        GpuPackageRef package = await scope.ImportPackageAsync(plan);
        GpuBufferRef mesh = package.GetExport<GpuBufferRef>("Mesh");
        GpuBufferHandle unrelated = manager.GetBufferRange(package.GetExport<GpuBufferRef>("Other")).Buffer;
        using GpuResourcePin pin = manager.Pin(mesh);

        scope.Dispose(); manager.Trim();

        Assert.Same(unrelated, Assert.Single(backend.Destroyed));
        Assert.Equal(2, manager.Statistics.BufferCount);
        pin.Dispose(); manager.Trim(); Assert.Equal(3, backend.Destroyed.Count);
    }

    [Fact]
    public void PreparedDependencyCycleIsRejectedBeforeGpuWork()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new GpuPackagePlan([
            new GpuPackageBuffer("first", Description, dependencies: ["second"]),
            new GpuPackageBuffer("second", Description, dependencies: ["first"])], []));

        Assert.Contains("cycle", error.Message);
    }

    [Fact]
    public async Task FailedPackageCreationDoesNotPublishPartialExports()
    {
        var backend = new ManagerTestBackend { CreationError = new InvalidOperationException("allocation failed") };
        await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        var plan = new GpuPackagePlan([new GpuPackageBuffer("mesh", Description)], [new("Mesh", "mesh")]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ImportPackageAsync(plan).AsTask());
        manager.Collect();

        Assert.Equal(0, manager.Statistics.ResourceCount);
        Assert.Empty(backend.TestQueue.Submitted);
    }
}
