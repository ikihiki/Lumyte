namespace Lumyte.Graphics.Native.Resources;

/// <summary>Caller-supplied producer scopes for a managed readback copy.</summary>
public readonly record struct GpuReadbackSynchronization(GpuStage BeforeStages, GpuAccess BeforeAccess)
{
    public static GpuReadbackSynchronization Default => new(GpuStage.All, GpuAccess.ShaderWrite | GpuAccess.CopyWrite);
}

public sealed partial class GpuResourceManager
{
    public async Task<byte[]> ReadBufferAsync(GpuBufferRef buffer, ulong offset = 0, ulong? length = null,
        CancellationToken cancellationToken = default, GpuReadbackSynchronization? synchronization = null)
    {
        RequireOpen(); cancellationToken.ThrowIfCancellationRequested();
        NativeGpuRange source = GetBufferRange(buffer, offset, length);
        int count = checked((int)source.Size);
        if (count == 0) { return []; }
        GpuResourceScope staging = CreateScope();
        GpuResourceBatch? batch = null;
        bool owned = false;
        Exception? primary = null;
        try
        {
            if (source.Region.Heap.Kind == NativeGpuMemoryKind.Readback)
            {
                batch = BeginBatch(); batch.Use(buffer);
                await batch.Submit().WaitAsync(cancellationToken);
                return CopyFromMapped(source, count);
            }
            GpuBufferRef target = staging.CreateBuffer(new(source.Size, NativeGpuMemoryKind.Readback, 4));
            NativeGpuRange destination = GetBufferRange(target);
            batch = BeginBatch(); batch.Use(buffer); batch.Own(staging); owned = true;
            NativeGpuCommandBuffer commands = batch.StartCommandRecording();
            GpuReadbackSynchronization sync = synchronization ?? GpuReadbackSynchronization.Default;
            commands.Barrier(sync.BeforeStages, sync.BeforeAccess, GpuStage.Copy, GpuAccess.CopyRead);
            commands.CopyMemory(source, destination);
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            await batch.Submit().WaitAsync(cancellationToken);
            return CopyFromMapped(destination, count);
        }
        catch (Exception error) { primary = error; throw; }
        finally { FinishTransfer(batch, staging, owned, primary); }
    }

    public async Task<byte[]> ReadTextureAsync(GpuTextureRef texture, NativeGpuTextureCopyFootprint footprint,
        uint rowCount, ulong rowBytes, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout,
        CancellationToken cancellationToken = default, GpuReadbackSynchronization? synchronization = null,
        ulong stagingAlignment = 512)
    {
        RequireOpen(); cancellationToken.ThrowIfCancellationRequested();
        NativeGpuTextureHandle source = GetTextureHandle(texture);
        NativeGpuTextureDescription description = Require(texture).TextureDescription!.Value;
        ulong images = Math.Max(footprint.LayerCount, footprint.Extent.Depth);
        if (images == 0 || rowCount == 0 || rowBytes == 0 || rowBytes > footprint.RowPitch)
        { throw new ArgumentException("Invalid CPU readback footprint.", nameof(footprint)); }
        ulong imageBytes = checked((rowCount - 1ul) * footprint.RowPitch + rowBytes);
        if (images > 1 && footprint.ImagePitch < imageBytes) { throw new ArgumentException("CPU image pitch is too small.", nameof(footprint)); }
        int count = checked((int)checked((images - 1) * footprint.ImagePitch + imageBytes));
        GpuResourceScope staging = CreateScope(); GpuResourceBatch? batch = null;
        bool owned = false; Exception? primary = null;
        try
        {
            GpuBufferRef target = staging.CreateBuffer(new((ulong)count, NativeGpuMemoryKind.Readback, stagingAlignment));
            NativeGpuRange destination = GetBufferRange(target);
            batch = BeginBatch(); batch.Use(texture); batch.Own(staging); owned = true;
            NativeGpuCommandBuffer commands = batch.StartCommandRecording();
            NativeGpuTextureView view = TransferView(source, description, footprint);
            GpuReadbackSynchronization sync = synchronization ?? GpuReadbackSynchronization.Default;
            commands.Barrier(sync.BeforeStages, sync.BeforeAccess, GpuStage.Copy, GpuAccess.CopyRead);
            if (Backend.Capabilities.ExplicitTextureTransitions) { commands.TextureTransition(view, beforeLayout, GpuTextureLayout.CopySource); }
            commands.CopyTextureToMemory(source, destination, footprint);
            if (Backend.Capabilities.ExplicitTextureTransitions) { commands.TextureTransition(view, GpuTextureLayout.CopySource, afterLayout); }
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            await batch.Submit().WaitAsync(cancellationToken);
            return CopyFromMapped(destination, count);
        }
        catch (Exception error) { primary = error; throw; }
        finally { FinishTransfer(batch, staging, owned, primary); }
    }
    private void FinishTransfer(GpuResourceBatch? batch, GpuResourceScope staging, bool owned, Exception? primary)
    {
        List<Exception> errors = [];
        try { batch?.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (!owned) { try { staging.Dispose(); } catch (Exception error) { errors.Add(error); } }
        try { Collect(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0)
        { if (primary is not null) { errors.Insert(0, primary); } throw new AggregateException("Transfer cleanup failed.", errors); }
    }
    private static unsafe byte[] CopyFromMapped(NativeGpuRange source, int count)
    {
        if (source.Region.CpuAddress == 0) { throw new InvalidOperationException("The readback allocation is not CPU mapped."); }
        byte[] result = new byte[count];
        nint address = checked(source.Region.CpuAddress + (nint)source.Offset);
        new ReadOnlySpan<byte>((void*)address, count).CopyTo(result);
        return result;
    }
}
