namespace Lumyte.Graphics.Native.Resources;

public sealed partial class GpuResourceManager
{
    private NativeGpuDescriptorHeap? resourceHeap;
    private NativeGpuDescriptorHeap? samplerHeap;
    private readonly Slots resourceSlots = new();
    private readonly Slots samplerSlots = new();
    private readonly Dictionary<(ResourceRecord, GpuTextureViewDescription), GpuViewRef> textureViews = [];
    private readonly Dictionary<(ResourceRecord, GpuBufferViewDescription), GpuViewRef> bufferViews = [];
    private readonly Dictionary<NativeGpuSamplerDescription, GpuSamplerRef> samplers = [];
    public NativeGpuDescriptorHeap ResourceDescriptorHeap
    { get { RequireOpen(); return resourceHeap ??= Backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, options.ResourceDescriptorCapacity); } }
    public NativeGpuDescriptorHeap SamplerDescriptorHeap
    { get { RequireOpen(); return samplerHeap ??= Backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, options.SamplerDescriptorCapacity); } }
    private void ReleaseDescriptorHeaps()
    {
        List<Exception> errors = [];
        if (resourceHeap is { } resources)
        { resourceHeap = null; try { Backend.DestroyDescriptorHeap(resources); } catch (Exception error) { errors.Add(error); } }
        if (samplerHeap is { } sampler)
        { samplerHeap = null; try { Backend.DestroyDescriptorHeap(sampler); } catch (Exception error) { errors.Add(error); } }
        if (errors.Count != 0) { throw new AggregateException("Descriptor heap cleanup failed.", errors); }
    }
    internal GpuViewRef GetView(GpuTextureRef texture, GpuTextureViewDescription description)
    {
        ResourceRecord owner = Require(texture);
        var key = (owner, description);
        if (textureViews.TryGetValue(key, out GpuViewRef? cached) && cached.Record.Alive) { return cached; }
        ResourceRecord? record = null;
        record = Register(() =>
        {
            textureViews.Remove(key);
            if (record!.RenderView is { } render) { Backend.DestroyRenderView(render); }
            if (record.ShaderIndex is { } index) { resourceSlots.Return(index); }
        }, owner);
        PrepareTextureView(record, owner.Texture!, description);
        GpuViewRef result = new(record);
        textureViews[key] = result;
        return result;
    }
    private void PrepareTextureView(ResourceRecord record, NativeGpuTextureHandle texture, GpuTextureViewDescription description)
    {
        if (description.Purpose is not (GpuTextureViewPurpose.Sampled or GpuTextureViewPurpose.Storage or GpuTextureViewPurpose.Attachment))
        { throw new ArgumentOutOfRangeException(nameof(description)); }
        if (description.Purpose != GpuTextureViewPurpose.Attachment && description.RenderFlags != NativeGpuRenderViewFlags.None)
        { throw new ArgumentException("Render flags require an attachment view.", nameof(description)); }
        NativeGpuTextureView view = new(texture, description.Dimension, description.Format, description.Aspect,
            description.BaseMip, description.MipCount, description.BaseLayer, description.LayerCount);
        record.TextureView = view;
        if (description.Purpose == GpuTextureViewPurpose.Attachment)
        { record.RenderView = Backend.CreateRenderView(view, description.RenderFlags); return; }
        NativeGpuDescriptorHeap heap = ResourceDescriptorHeap;
        uint slot = resourceSlots.Rent(heap.Capacity);
        try
        {
            Backend.WriteTextureDescriptor(heap, slot, view, description.Purpose == GpuTextureViewPurpose.Storage
                ? NativeGpuTextureDescriptorType.Storage : NativeGpuTextureDescriptorType.Sampled);
            record.ShaderIndex = slot;
        }
        catch { resourceSlots.Return(slot); throw; }
    }
    internal GpuViewRef GetView(GpuBufferRef buffer, GpuBufferViewDescription description)
    {
        ResourceRecord owner = Require(buffer);
        var key = (owner, description);
        if (bufferViews.TryGetValue(key, out GpuViewRef? cached) && cached.Record.Alive) { return cached; }
        NativeGpuRange range = GetBufferRange(buffer, description.Offset, description.Length);
        NativeGpuDescriptorHeap heap = ResourceDescriptorHeap;
        uint slot = resourceSlots.Rent(heap.Capacity);
        try { Backend.WriteBufferDescriptor(heap, slot, range, description.Access); }
        catch { resourceSlots.Return(slot); throw; }
        ResourceRecord record = Register(() => { bufferViews.Remove(key); resourceSlots.Return(slot); }, owner);
        record.ShaderIndex = slot;
        GpuViewRef result = new(record); bufferViews[key] = result; return result;
    }
    internal GpuSamplerRef GetSampler(NativeGpuSamplerDescription description)
    {
        if (samplers.TryGetValue(description, out GpuSamplerRef? cached) && cached.Record.Alive) { return cached; }
        NativeGpuDescriptorHeap heap = SamplerDescriptorHeap;
        uint slot = samplerSlots.Rent(heap.Capacity);
        try { Backend.WriteSamplerDescriptor(heap, slot, description); }
        catch { samplerSlots.Return(slot); throw; }
        ResourceRecord record = Register(() => { samplers.Remove(description); samplerSlots.Return(slot); });
        record.ShaderIndex = slot;
        GpuSamplerRef result = new(record); samplers[description] = result; return result;
    }
    private sealed class Slots
    {
        private readonly Stack<uint> available = [];
        private uint next;
        internal uint Used { get; private set; }
        internal uint Rent(uint capacity)
        {
            uint slot;
            if (available.Count != 0) { slot = available.Pop(); }
            else { if (next == capacity) { throw new InvalidOperationException("The manager descriptor storage is full."); } slot = next++; }
            Used++; return slot;
        }
        internal void Return(uint index) { available.Push(index); Used--; }
    }
}
