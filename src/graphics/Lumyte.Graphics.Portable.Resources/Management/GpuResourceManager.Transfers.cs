namespace Lumyte.Graphics.Portable.Resources;

public sealed partial class GpuResourceManager
{
    /// <summary>Maps a borrowed buffer while holding its managed identity until successful unmapping.</summary>
    public async ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferRef buffer, GpuMapMode mode,
        ulong offset = 0, ulong? length = null, CancellationToken cancellationToken = default)
    {
        CheckOpen(); cancellationToken.ThrowIfCancellationRequested();
        GpuBufferRange range = GetBufferRange(buffer, offset, length);
        GpuResourceUse use = AcquireUse(buffer);
        GpuMappedBufferRange mapping;
        try { mapping = await Backend.MapBufferAsync(range.Buffer, mode, range.Offset, range.Length!.Value); }
        catch { use.Dispose(); throw; }
        var managed = new ManagedMapping(mapping, use);
        if (cancellationToken.IsCancellationRequested)
        { managed.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
        return managed;
    }
    /// <summary>Copies owned CPU bytes using manager staging; cancellation cancels only waiting, never GPU ownership.</summary>
    public async ValueTask UploadBufferAsync(GpuBufferRef destination, ReadOnlyMemory<byte> data,
        ulong destinationOffset = 0, CancellationToken cancellationToken = default)
    {
        CheckOpen(); Check(destination); cancellationToken.ThrowIfCancellationRequested();
        byte[] owned = data.ToArray();
        GpuBufferRange target = GetBufferRange(destination, destinationOffset, (ulong)owned.Length);
        if (destination.Description.Usage.HasFlag(GpuBufferUsage.MapWrite))
        {
            // MapWrite resources intentionally cannot also be WebGPU copy destinations. Mapping
            // preserves the caller's description and CPU visibility; external GPU synchronization remains explicit.
            using GpuMappedBufferRange mapping = await MapBufferAsync(destination, GpuMapMode.Write, cancellationToken: cancellationToken);
            owned.CopyTo(mapping.Memory.Span.Slice(checked((int)destinationOffset), owned.Length));
            return;
        }
        using GpuResourceScope stagingScope = CreateScope();
        using GpuResourceUse targetUse = AcquireUse(destination);
        GpuBufferRef staging = stagingScope.CreateBuffer(new(AlignTransfer((ulong)owned.Length), GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
        await WriteStagingAsync(staging, owned, cancellationToken);
        using GpuResourceBatch batch = BeginBatch();
        batch.Use(destination); batch.Use(staging);
        batch.StartCommandRecording().CopyBuffer(GetBufferRange(staging, 0, (ulong)owned.Length), target);
        GpuSubmissionToken token = SubmitTransfer(batch);
        batch.Dispose();
        await token.WaitAsync(cancellationToken);
    }
    public async ValueTask<byte[]> ReadBufferAsync(GpuBufferRef source, ulong offset = 0, ulong? length = null,
        CancellationToken cancellationToken = default)
    {
        CheckOpen(); cancellationToken.ThrowIfCancellationRequested();
        GpuBufferRange range = GetBufferRange(source, offset, length);
        int resultLength = checked((int)range.Length!.Value);
        using GpuResourceScope stagingScope = CreateScope();
        using GpuResourceUse sourceUse = AcquireUse(source);
        GpuBufferRef staging = stagingScope.CreateBuffer(new(AlignTransfer((ulong)resultLength), GpuBufferUsage.MapRead | GpuBufferUsage.CopyDestination));
        using GpuResourceBatch batch = BeginBatch();
        batch.Use(source); batch.Use(staging);
        batch.StartCommandRecording().CopyBuffer(range, GetBufferRange(staging, 0, (ulong)resultLength));
        GpuSubmissionToken token = SubmitTransfer(batch);
        batch.Dispose();
        await token.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using GpuMappedBufferRange mapping = await MapBufferAsync(staging, GpuMapMode.Read);
        return mapping.ReadOnlyMemory.Span[..resultLength].ToArray();
    }
    public async ValueTask UploadTextureAsync(GpuTextureRef destination, GpuTextureUpload upload,
        CancellationToken cancellationToken = default)
    {
        CheckOpen(); Check(destination); ArgumentNullException.ThrowIfNull(upload); cancellationToken.ThrowIfCancellationRequested();
        ulong requiredBytes = upload.Footprint.RequiredBytes(destination.Description.Format);
        if (requiredBytes > (ulong)upload.Data.Length) { throw new ArgumentException("The upload bytes do not cover the CPU footprint.", nameof(upload)); }
        using GpuResourceScope stagingScope = CreateScope();
        using GpuResourceUse targetUse = AcquireUse(destination);
        GpuBufferRef staging = stagingScope.CreateBuffer(new(AlignTransfer(requiredBytes), GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
        await WriteStagingAsync(staging, upload.Data[..checked((int)requiredBytes)].ToArray(), cancellationToken);
        using GpuResourceBatch batch = BeginBatch();
        batch.Use(destination); batch.Use(staging);
        batch.StartCommandRecording().CopyBufferToTexture(GetBufferRange(staging, 0, requiredBytes), GetTextureHandle(destination), upload.Footprint);
        GpuSubmissionToken token = SubmitTransfer(batch);
        batch.Dispose();
        await token.WaitAsync(cancellationToken);
    }
    public async ValueTask<byte[]> ReadTextureAsync(GpuTextureRef source, GpuTextureCopyFootprint footprint,
        CancellationToken cancellationToken = default)
    {
        CheckOpen(); Check(source); cancellationToken.ThrowIfCancellationRequested();
        ulong requiredBytes = footprint.RequiredBytes(source.Description.Format);
        int resultLength = checked((int)requiredBytes);
        using GpuResourceScope stagingScope = CreateScope();
        using GpuResourceUse sourceUse = AcquireUse(source);
        GpuBufferRef staging = stagingScope.CreateBuffer(new(AlignTransfer(requiredBytes), GpuBufferUsage.MapRead | GpuBufferUsage.CopyDestination));
        using GpuResourceBatch batch = BeginBatch();
        batch.Use(source); batch.Use(staging);
        batch.StartCommandRecording().CopyTextureToBuffer(GetTextureHandle(source), footprint, GetBufferRange(staging, 0, requiredBytes));
        GpuSubmissionToken token = SubmitTransfer(batch);
        batch.Dispose();
        await token.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using GpuMappedBufferRange mapping = await MapBufferAsync(staging, GpuMapMode.Read);
        return mapping.ReadOnlyMemory.Span[..resultLength].ToArray();
    }
    private static GpuSubmissionToken SubmitTransfer(GpuResourceBatch batch)
    {
        try { return batch.Submit(); }
        catch (Exception primary)
        {
            try { batch.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Submission and recording cleanup failed.", primary, cleanup); }
            throw;
        }
    }
    private async ValueTask WriteStagingAsync(GpuBufferRef staging, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        using GpuMappedBufferRange mapping = await MapBufferAsync(staging, GpuMapMode.Write);
        cancellationToken.ThrowIfCancellationRequested();
        mapping.Memory.Span.Clear(); data.Span.CopyTo(mapping.Memory.Span);
    }
    private static ulong AlignTransfer(ulong size) => checked((size + 3) & ~3UL);

    internal async ValueTask<GpuPackageRef> ImportPackageAsync(GpuResourceScope destination, GpuPackagePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        using GpuResourceScope temporary = CreateScope();
        var imported = new Dictionary<string, GpuResourceRef>(StringComparer.Ordinal);
        foreach (GpuPackageResource item in plan.Resources)
        {
            GpuResourceRef reference = item switch
            {
                GpuPackageBuffer buffer => temporary.CreateBuffer(buffer.Description),
                GpuPackageTexture texture => temporary.CreateTexture(texture.Description),
                _ => throw new NotSupportedException("Unknown prepared resource kind."),
            };
            imported.Add(item.Id, reference);
        }
        foreach (GpuPackageResource item in plan.Resources)
        {
            ResourceRecord record = imported[item.Id].Record;
            ResourceRecord[] dependencies = item.Dependencies.Select(id => imported[id].Record).ToArray();
            record.Dependencies = dependencies;
            foreach (ResourceRecord dependency in dependencies) { dependency.Holds++; }
        }
        foreach (GpuPackageResource item in plan.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is GpuPackageBuffer buffer && !buffer.Data.IsEmpty)
            { await UploadBufferAsync((GpuBufferRef)imported[item.Id], buffer.Data.ToArray(), buffer.DestinationOffset, cancellationToken); }
            else if (item is GpuPackageTexture texture)
            {
                foreach (GpuTextureUpload upload in texture.Uploads)
                { await UploadTextureAsync((GpuTextureRef)imported[item.Id], upload, cancellationToken); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested(); destination.CheckOpen();
        var exports = plan.Exports.ToDictionary(item => item.Id, item => imported[item.ResourceId], StringComparer.Ordinal);
        var packageRecord = new ResourceRecord(this);
        var package = new GpuPackageRef(packageRecord, exports);
        Register(packageRecord, package, imported.Values.Select(item => item.Record).ToArray());
        return destination.Hold(package);
    }
}

internal sealed class ManagedMapping(GpuMappedBufferRange mapping, GpuResourceUse use) : GpuMappedBufferRange, IManagedBatchLease
{
    private bool disposed;
    private bool transferred;
    private Exception? failure;
    public override Memory<byte> Memory { get { ObjectDisposedException.ThrowIf(disposed || transferred || failure is not null, this); return mapping.Memory; } }
    public override ReadOnlyMemory<byte> ReadOnlyMemory { get { ObjectDisposedException.ThrowIf(disposed || transferred || failure is not null, this); return mapping.ReadOnlyMemory; } }
    ResourceRecord IManagedBatchLease.PrepareTransfer(GpuResourceManager manager)
    {
        ObjectDisposedException.ThrowIf(disposed || transferred || failure is not null, this);
        return ((IManagedBatchLease)use).PrepareTransfer(manager);
    }
    void IManagedBatchLease.Transfer() { ((IManagedBatchLease)use).Transfer(); transferred = true; }
    void IManagedBatchLease.ReleaseFromBatch() { CloseMapping(); ((IManagedBatchLease)use).ReleaseFromBatch(); }
    public override void Dispose()
    {
        if (transferred) { throw new InvalidOperationException("The mapping belongs to a batch."); }
        CloseMapping(); use.Dispose();
    }
    private void CloseMapping()
    {
        if (disposed) { return; }
        if (failure is not null) { throw failure; }
        try { mapping.Dispose(); }
        catch (Exception error) { failure = error; throw; }
        disposed = true;
    }
}
