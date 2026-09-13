namespace Lumyte.Graphics.Native.Resources;

public sealed partial class GpuResourceManager
{
    private NativeGpuMemoryRequirements BufferRequirements(GpuBufferDescription description)
    {
        if (description.Size == 0 || description.Alignment == 0) { throw new ArgumentOutOfRangeException(nameof(description)); }
        if (!bufferRequirements.TryGetValue(description, out NativeGpuMemoryRequirements requirements))
        {
            requirements = Backend.GetLinearMemoryRequirements(description.Size, description.MemoryKind);
            bufferRequirements.Add(description, requirements);
        }
        return requirements;
    }
    private NativeGpuMemoryRequirements TextureRequirements(NativeGpuTextureDescription description, NativeGpuMemoryKind kind)
    {
        if (!textureRequirements.TryGetValue((description, kind), out NativeGpuMemoryRequirements requirements))
        {
            requirements = Backend.GetTextureMemoryRequirements(description, kind);
            textureRequirements.Add((description, kind), requirements);
        }
        return requirements;
    }
    internal GpuBufferRef CreateBuffer(GpuBufferDescription description)
    {
        NativeGpuMemoryRequirements requirements = BufferRequirements(description);
        string purpose = description.MemoryKind switch { NativeGpuMemoryKind.CpuVisible => "upload", NativeGpuMemoryKind.Readback => "readback", _ => "linear" };
        GpuMemorySlice slice = Allocate(purpose, requirements, description.MemoryKind,
            CommonAlignment(requirements.Alignment, description.Alignment));
        NativeGpuLinearRegion region;
        try { region = Backend.CreateLinearRegion(description.Size, slice.Heap, slice.Offset); }
        catch { Release(purpose, slice); throw; }
        ResourceRecord record = Register(() => { Backend.DestroyLinearRegion(region); Release(purpose, slice); });
        record.Buffer = new(region, 0, description.Size);
        return new(record);
    }
    internal GpuTextureRef CreateTexture(NativeGpuTextureDescription description, NativeGpuMemoryKind kind,
        GpuTextureViewDescription? defaultView)
    {
        NativeGpuMemoryRequirements requirements = TextureRequirements(description, kind);
        string purpose = defaultView?.Purpose == GpuTextureViewPurpose.Attachment ? "attachment" : "texture";
        GpuMemorySlice slice = Allocate(purpose, requirements, kind, requirements.Alignment);
        NativeGpuTextureHandle texture;
        try { texture = Backend.CreateTexture(description, slice.Heap, slice.Offset); }
        catch { Release(purpose, slice); throw; }
        ResourceRecord? record = null;
        record = Register(() =>
        {
            if (defaultView is { } defaultDescription) { textureViews.Remove((record!, defaultDescription)); }
            if (record!.RenderView is { } renderView) { Backend.DestroyRenderView(renderView); }
            Backend.DestroyTexture(texture);
            if (record.ShaderIndex is { } index) { resourceSlots.Return(index); }
            Release(purpose, slice);
        });
        record.Texture = texture;
        record.TextureDescription = description;
        GpuTextureRef reference = new(record);
        if (defaultView is { } view)
        {
            PrepareTextureView(record, texture, view);
            textureViews[(record, view)] = new GpuViewRef(record);
        }
        return reference;
    }
}
