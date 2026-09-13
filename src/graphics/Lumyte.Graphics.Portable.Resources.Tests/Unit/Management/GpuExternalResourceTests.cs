namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

public sealed class GpuExternalResourceTests
{
    [Fact]
    public async Task ImportedTextureIsReturnedThroughItsLeaseInsteadOfThePool()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend);
        var description = new GpuTextureDescription(GpuTextureDimension.Texture2D, 2, 2, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled);
        GpuTextureHandle raw = backend.CreateTexture(description); var lease = new Lease();
        using GpuResourceScope scope = manager.CreateScope();
        GpuTextureRef texture = scope.ImportTexture(raw, description, lease);
        scope.GetView(texture);

        scope.Dispose(); manager.Collect();

        Assert.Equal(1, lease.Returns); Assert.DoesNotContain(raw, backend.Destroyed);
        using GpuResourceScope another = manager.CreateScope();
        another.ImportTexture(raw, description, new Lease());
        another.Dispose(); manager.Collect();
    }

    [Fact]
    public async Task DuplicateRawImportDoesNotTakeTheSecondLease()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend);
        using GpuResourceScope scope = manager.CreateScope(); var description = new GpuBufferDescription(8, GpuBufferUsage.Storage);
        GpuBufferHandle raw = backend.CreateBuffer(description); var second = new Lease();
        scope.ImportBuffer(raw, description, new Lease());

        ArgumentException error = Assert.Throws<ArgumentException>(() => scope.ImportBuffer(raw, description, second));

        Assert.Equal("handle", error.ParamName); Assert.Equal(0, second.Returns);
    }

    [Fact]
    public async Task ExternalLeaseWaitsForGpuUseEvenAfterAnUncertainSubmission()
    {
        var backend = new ManagerTestBackend(); backend.TestQueue.AutoComplete = false;
        backend.TestQueue.RegisterBeforeThrow = true; backend.TestQueue.SubmitError = new InvalidOperationException("handoff");
        await using var manager = new GpuResourceManager(backend); using GpuResourceBatch batch = manager.BeginBatch();
        batch.StartCommandRecording(); var lease = new Lease();
        GpuSubmissionException failure = Assert.Throws<GpuSubmissionException>(() => batch.Submit()); batch.Dispose();

        manager.RetainUntilSubmissionEnds(failure.Completion, lease); manager.Collect();
        Assert.False(manager.OwnsSubmission(failure.Completion)); Assert.Equal(0, lease.Returns);
        backend.TestQueue.Complete(0); manager.Collect();

        Assert.Equal(1, lease.Returns);
    }

    private sealed class Lease : IDisposable
    { internal int Returns { get; private set; } public void Dispose() => Returns++; }
}
