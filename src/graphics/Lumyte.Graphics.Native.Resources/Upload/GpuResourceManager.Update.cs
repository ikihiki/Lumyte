namespace Lumyte.Graphics.Native.Resources;

/// <summary>Caller-declared resource access before and after a managed upload.</summary>
public readonly record struct GpuUploadSynchronization(GpuStage BeforeStages, GpuAccess BeforeAccess,
    GpuStage AfterStages, GpuAccess AfterAccess)
{
    public static GpuUploadSynchronization Default => new(GpuStage.All, GpuAccess.ShaderRead | GpuAccess.ShaderWrite | GpuAccess.CopyRead | GpuAccess.CopyWrite,
        GpuStage.All, GpuAccess.ShaderRead);
}

public sealed partial class GpuResourceManager
{
    public async ValueTask UploadBufferAsync(GpuBufferRef destination, ReadOnlyMemory<byte> data,
        ulong destinationOffset = 0, CancellationToken cancellationToken = default,
        GpuUploadSynchronization? synchronization = null)
    {
        RequireOpen(); cancellationToken.ThrowIfCancellationRequested();
        NativeGpuRange target = GetBufferRange(destination, destinationOffset, (ulong)data.Length);
        if (data.IsEmpty) { return; }
        byte[] payload = data.ToArray();
        GpuResourceScope staging = CreateScope(); GpuResourceBatch? batch = null;
        bool owned = false; Exception? primary = null;
        try
        {
            batch = BeginBatch(); batch.Use(destination);
            if (target.Region.Heap.Kind == NativeGpuMemoryKind.CpuVisible)
            {
                // End preceding manager-queue uses before modifying upload memory on the CPU.
                // The caller still covers other queues and external/unsubmitted references.
                await batch.Submit().WaitAsync(cancellationToken);
                CopyToMapped(target, payload);
                return;
            }
            GpuBufferRef source = staging.CreateBuffer(new((ulong)payload.Length, NativeGpuMemoryKind.CpuVisible, 4));
            NativeGpuRange sourceRange = GetBufferRange(source); CopyToMapped(sourceRange, payload);
            batch.Own(staging); owned = true;
            NativeGpuCommandBuffer commands = batch.StartCommandRecording();
            GpuUploadSynchronization sync = synchronization ?? GpuUploadSynchronization.Default;
            commands.Barrier(GpuStage.Host, GpuAccess.HostWrite, GpuStage.Copy, GpuAccess.CopyRead);
            commands.Barrier(sync.BeforeStages, sync.BeforeAccess, GpuStage.Copy, GpuAccess.CopyWrite);
            commands.CopyMemory(sourceRange, target);
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, sync.AfterStages, sync.AfterAccess);
            await batch.Submit().WaitAsync(cancellationToken);
        }
        catch (Exception error) { primary = error; throw; }
        finally { FinishTransfer(batch, staging, owned, primary); }
    }

    public async ValueTask UploadTextureAsync(GpuTextureRef destination, GpuTextureUpload upload,
        CancellationToken cancellationToken = default, GpuUploadSynchronization? synchronization = null)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(upload); cancellationToken.ThrowIfCancellationRequested();
        ResourceRecord target = Require(destination);
        NativeGpuTextureHandle texture = target.Texture!;
        NativeGpuTextureView view = TransferView(texture, target.TextureDescription!.Value, upload.Footprint);
        GpuResourceScope staging = CreateScope(); GpuResourceBatch? batch = null;
        bool owned = false; Exception? primary = null;
        try
        {
            GpuBufferRef source = staging.CreateBuffer(new((ulong)upload.Data.Length, NativeGpuMemoryKind.CpuVisible, upload.StagingAlignment));
            NativeGpuRange range = GetBufferRange(source); CopyToMapped(range, upload.Data);
            batch = BeginBatch(); batch.Use(destination); batch.Own(staging); owned = true;
            NativeGpuCommandBuffer commands = batch.StartCommandRecording();
            GpuUploadSynchronization sync = synchronization ?? GpuUploadSynchronization.Default;
            commands.Barrier(GpuStage.Host, GpuAccess.HostWrite, GpuStage.Copy, GpuAccess.CopyRead);
            commands.Barrier(sync.BeforeStages, sync.BeforeAccess, GpuStage.Copy, GpuAccess.CopyWrite);
            bool transitions = Backend.Capabilities.ExplicitTextureTransitions;
            if (upload.BeforeLayout == GpuTextureLayout.Undefined)
            { commands.DiscardTexture(view, transitions ? GpuTextureLayout.CopyDestination : GpuTextureLayout.General); }
            else if (transitions) { commands.TextureTransition(view, upload.BeforeLayout, GpuTextureLayout.CopyDestination); }
            commands.CopyMemoryToTexture(range, texture, upload.Footprint);
            if (transitions) { commands.TextureTransition(view, GpuTextureLayout.CopyDestination, upload.AfterLayout); }
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, sync.AfterStages, sync.AfterAccess);
            await batch.Submit().WaitAsync(cancellationToken);
        }
        catch (Exception error) { primary = error; throw; }
        finally { FinishTransfer(batch, staging, owned, primary); }
    }
}
