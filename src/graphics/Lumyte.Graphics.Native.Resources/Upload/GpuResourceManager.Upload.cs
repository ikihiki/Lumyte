using System.Collections.ObjectModel;

namespace Lumyte.Graphics.Native.Resources;

public sealed partial class GpuResourceManager
{
    internal async Task<GpuPackageRef> ImportPackageAsync(GpuResourceScope destination, GpuPackagePlan plan,
        GpuPackagePlacement placement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        if (placement is not (GpuPackagePlacement.Pools or GpuPackagePlacement.SingleAllocation)) { throw new ArgumentOutOfRangeException(nameof(placement)); }
        GpuResourceScope temporary = CreateScope();
        GpuResourceScope staging = CreateScope();
        GpuResourceBatch? batch = null;
        bool stagingOwned = false;
        Exception? primaryFailure = null;
        try
        {
            Dictionary<string, GpuResourceRef> references = placement == GpuPackagePlacement.SingleAllocation
                ? CreateSingleAllocations(temporary, plan) : CreatePooledPackage(temporary, plan);
            foreach (GpuPackageResource entry in plan.Resources)
            {
                foreach (string dependency in entry.Dependencies)
                { AddDependency(references[entry.Id].Record, references[dependency].Record); }
            }
            Dictionary<string, GpuResourceRef> exports = new(StringComparer.Ordinal);
            foreach (var export in plan.Exports) { exports.Add(export.Key, references[export.Value]); }
            ResourceRecord packageRecord = Register(static () => { }, references.Values.Select(reference => reference.Record).ToArray());
            GpuPackageRef package = temporary.Hold(new GpuPackageRef(packageRecord, new ReadOnlyDictionary<string, GpuResourceRef>(exports)));
            List<Action<NativeGpuCommandBuffer>> transfers = [];
            foreach (GpuPackageResource entry in plan.Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is GpuPackageBuffer buffer && !buffer.Data.IsEmpty)
                {
                    if (buffer.Description.MemoryKind == NativeGpuMemoryKind.CpuVisible)
                    {
                        CopyToMapped(GetBufferRange((GpuBufferRef)references[entry.Id], buffer.DestinationOffset,
                            (ulong)buffer.Data.Length), buffer.Data);
                        continue;
                    }
                    GpuBufferRef source = staging.CreateBuffer(new((ulong)buffer.Data.Length, NativeGpuMemoryKind.CpuVisible, 4));
                    NativeGpuRange sourceRange = GetBufferRange(source);
                    CopyToMapped(sourceRange, buffer.Data);
                    NativeGpuRange target = GetBufferRange((GpuBufferRef)references[entry.Id], buffer.DestinationOffset, (ulong)buffer.Data.Length);
                    transfers.Add(commands => commands.CopyMemory(sourceRange, target));
                }
                else if (entry is GpuPackageTexture texture)
                {
                    NativeGpuTextureHandle target = GetTextureHandle((GpuTextureRef)references[entry.Id]);
                    foreach (GpuTextureUpload upload in texture.Uploads)
                    {
                        GpuBufferRef source = staging.CreateBuffer(new((ulong)upload.Data.Length, NativeGpuMemoryKind.CpuVisible, upload.StagingAlignment));
                        NativeGpuRange sourceRange = GetBufferRange(source); CopyToMapped(sourceRange, upload.Data);
                        NativeGpuTextureView view = TransferView(target, texture.Description, upload.Footprint);
                        transfers.Add(commands =>
                        {
                            bool transitions = Backend.Capabilities.ExplicitTextureTransitions;
                            if (upload.BeforeLayout == GpuTextureLayout.Undefined)
                            { commands.DiscardTexture(view, transitions ? GpuTextureLayout.CopyDestination : GpuTextureLayout.General); }
                            else if (transitions) { commands.TextureTransition(view, upload.BeforeLayout, GpuTextureLayout.CopyDestination); }
                            commands.CopyMemoryToTexture(sourceRange, target, upload.Footprint);
                            if (transitions) { commands.TextureTransition(view, GpuTextureLayout.CopyDestination, upload.AfterLayout); }
                        });
                    }
                }
            }
            if (transfers.Count != 0)
            {
                batch = BeginBatch(); batch.Use(package); batch.Own(staging); stagingOwned = true;
                NativeGpuCommandBuffer commands = batch.StartCommandRecording();
                commands.Barrier(GpuStage.Host, GpuAccess.HostWrite, GpuStage.Copy, GpuAccess.CopyRead);
                foreach (Action<NativeGpuCommandBuffer> transfer in transfers) { transfer(commands); }
                commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.All, GpuAccess.ShaderRead);
                cancellationToken.ThrowIfCancellationRequested();
                GpuSubmissionToken completion = batch.Submit();
                await completion.WaitAsync(cancellationToken);
            }
            return destination.Hold(package);
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            List<Exception> cleanup = [];
            try { batch?.Dispose(); } catch (Exception error) { cleanup.Add(error); }
            if (!stagingOwned) { try { staging.Dispose(); } catch (Exception error) { cleanup.Add(error); } }
            try { temporary.Dispose(); } catch (Exception error) { cleanup.Add(error); }
            try { Collect(); } catch (Exception error) { cleanup.Add(error); }
            if (cleanup.Count != 0)
            {
                if (primaryFailure is not null) { cleanup.Insert(0, primaryFailure); }
                throw new AggregateException("Package import and cleanup failed.", cleanup);
            }
        }
    }
    private Dictionary<string, GpuResourceRef> CreatePooledPackage(GpuResourceScope scope, GpuPackagePlan plan)
    {
        Dictionary<string, GpuResourceRef> references = new(StringComparer.Ordinal);
        foreach (GpuPackageResource entry in plan.Resources)
        {
            references.Add(entry.Id, entry switch
            {
                GpuPackageBuffer buffer => scope.CreateBuffer(buffer.Description),
                GpuPackageTexture texture => scope.CreateTexture(texture.Description, texture.MemoryKind, texture.DefaultView),
                _ => throw new ArgumentException("Unsupported package resource.", nameof(plan)),
            });
        }
        return references;
    }
    private Dictionary<string, GpuResourceRef> CreateSingleAllocations(GpuResourceScope scope, GpuPackagePlan plan)
    {
        Dictionary<string, GpuResourceRef> references = new(StringComparer.Ordinal);
        foreach (var group in plan.Resources.GroupBy(resource => resource.AllocationGroup, StringComparer.Ordinal))
        {
            GpuPackageResource[] entries = group.ToArray();
            NativeGpuMemoryRequirements[] requirements = new NativeGpuMemoryRequirements[entries.Length];
            NativeGpuMemoryCompatibility[] tokens = new NativeGpuMemoryCompatibility[entries.Length];
            ulong[] offsets = new ulong[entries.Length];
            ulong capacity = 0, alignment = 1;
            NativeGpuMemoryKind? kind = null;
            for (int index = 0; index < entries.Length; index++)
            {
                NativeGpuMemoryKind entryKind;
                ulong logicalAlignment;
                if (entries[index] is GpuPackageBuffer buffer)
                { requirements[index] = BufferRequirements(buffer.Description); entryKind = buffer.Description.MemoryKind; logicalAlignment = buffer.Description.Alignment; }
                else if (entries[index] is GpuPackageTexture texture)
                { requirements[index] = TextureRequirements(texture.Description, texture.MemoryKind); entryKind = texture.MemoryKind; logicalAlignment = 1; }
                else { throw new ArgumentException("Unsupported package resource.", nameof(plan)); }
                if (kind.HasValue && kind != entryKind) { throw new ArgumentException("One allocation group must have one memory kind.", nameof(plan)); }
                kind = entryKind;
                ulong entryAlignment = CommonAlignment(requirements[index].Alignment, logicalAlignment);
                alignment = CommonAlignment(alignment, entryAlignment);
                offsets[index] = Align(capacity, entryAlignment);
                capacity = checked(offsets[index] + requirements[index].Size);
                tokens[index] = requirements[index].Compatibility;
            }
            const string purpose = "package";
            if (!arenas.TryGetValue(purpose, out GpuMemoryArena? arena)) { arenas.Add(purpose, arena = new(Backend, options.BlockSize)); }
            GpuMemorySlice slice = arena.Allocate(capacity, alignment, kind!.Value, tokens);
            heaps.TryGetValue(slice.Heap, out int count); heaps[slice.Heap] = count + 1;
            List<Action> destroy = new(entries.Length);
            Exception[] cleanupErrors = new Exception[entries.Length];
            ResourceRecord groupRecord = Register(() =>
            {
                int errorCount = 0;
                for (int index = destroy.Count - 1; index >= 0; index--)
                { try { destroy[index](); } catch (Exception error) { cleanupErrors[errorCount++] = error; } }
                if (errorCount != 0) { throw new AggregateException("Allocation-group resource destruction failed.", cleanupErrors.Take(errorCount)); }
                Release(purpose, slice);
            });
            scope.Hold(new GpuPackageRef(groupRecord, new Dictionary<string, GpuResourceRef>()));
            for (int index = 0; index < entries.Length; index++)
            {
                ulong offset = checked(slice.Offset + offsets[index]);
                if (entries[index] is GpuPackageBuffer buffer)
                {
                    NativeGpuLinearRegion region = Backend.CreateLinearRegion(buffer.Description.Size, slice.Heap, offset);
                    destroy.Add(() => Backend.DestroyLinearRegion(region));
                    ResourceRecord record = Register(static () => { }, groupRecord);
                    record.Buffer = new(region, 0, buffer.Description.Size);
                    references.Add(buffer.Id, scope.Hold(new GpuBufferRef(record)));
                }
                else if (entries[index] is GpuPackageTexture texture)
                {
                    NativeGpuTextureHandle handle = Backend.CreateTexture(texture.Description, slice.Heap, offset);
                    destroy.Add(() => Backend.DestroyTexture(handle));
                    ResourceRecord? record = null;
                    record = Register(() =>
                    {
                        if (texture.DefaultView is { } defaultDescription) { textureViews.Remove((record!, defaultDescription)); }
                        if (record!.RenderView is { } render) { Backend.DestroyRenderView(render); }
                        if (record.ShaderIndex is { } slot) { resourceSlots.Return(slot); }
                    }, groupRecord);
                    record.Texture = handle;
                    record.TextureDescription = texture.Description;
                    GpuTextureRef reference = scope.Hold(new GpuTextureRef(record));
                    references.Add(texture.Id, reference);
                    if (texture.DefaultView is { } view)
                    {
                        PrepareTextureView(record, handle, view);
                        textureViews[(record, view)] = new GpuViewRef(record);
                    }
                }
            }
        }
        return references;
    }
    private static NativeGpuTextureView TransferView(NativeGpuTextureHandle texture,
        NativeGpuTextureDescription description, NativeGpuTextureCopyFootprint footprint)
    {
        NativeGpuTextureViewDimension dimension = description.Dimension switch
        {
            NativeGpuTextureDimension.OneD => NativeGpuTextureViewDimension.OneD,
            NativeGpuTextureDimension.ThreeD => NativeGpuTextureViewDimension.ThreeD,
            _ => description.LayerCount > 1 ? NativeGpuTextureViewDimension.TwoDArray : NativeGpuTextureViewDimension.TwoD,
        };
        bool volume = description.Dimension == NativeGpuTextureDimension.ThreeD;
        return new(texture, dimension, description.Format, footprint.Aspect, footprint.Mip, 1,
            volume ? 0 : footprint.BaseLayer, volume ? 1 : footprint.LayerCount);
    }
    private static unsafe void CopyToMapped(NativeGpuRange target, ReadOnlySpan<byte> data)
    {
        if ((ulong)data.Length > target.Size) { throw new ArgumentException("The payload exceeds its CPU mapping.", nameof(data)); }
        if (target.Region.CpuAddress == 0) { throw new InvalidOperationException("The upload allocation is not CPU mapped."); }
        nint address = checked(target.Region.CpuAddress + (nint)target.Offset);
        data.CopyTo(new Span<byte>((void*)address, data.Length));
    }
}
