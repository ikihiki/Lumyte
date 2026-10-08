using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class CommandBuffer : GpuResource
{
    private HashSet<GpuResource> _resources;
    private bool _submitted;
    private List<MaterialTransfer> _materialTransfers;

    internal CommandBuffer(WgpuDevice owner, WGPUCommandBufferImpl* handle, HashSet<GpuResource> resources, List<MaterialTransfer> transfers)
        : base(owner)
    {
        Handle = handle;
        _resources = resources;
        _materialTransfers = transfers;
    }

    internal WGPUCommandBufferImpl* Handle { get; }

    internal void MarkSubmitted()
    {
        if (_submitted)
        {
            throw new InvalidOperationException("Command buffer was already submitted.");
        }

        _submitted = true;
    }

    internal void CheckUnsubmitted()
    {
        if (_submitted)
        {
            throw new InvalidOperationException("Command buffer was already submitted.");
        }
    }

    internal HashSet<GpuResource> TakeResources()
    {
        HashSet<GpuResource> resources = _resources;
        _resources = [];
        return resources;
    }

    internal List<MaterialTransfer> TakeMaterialTransfers()
    {
        List<MaterialTransfer> transfers = _materialTransfers;
        _materialTransfers = [];
        return transfers;
    }

    protected override void ReleaseNative()
    {
        foreach (MaterialTransfer t in _materialTransfers)
        {
            t.Region.Buffer.CancelMaterial(t.Region);
        }

        _materialTransfers.Clear();
        WGPU.wgpuCommandBufferRelease(Handle);
        foreach (GpuResource resource in _resources)
        {
            resource.ReleaseLease();
        }

        _resources.Clear();
    }
}
