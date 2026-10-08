using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class CommandBuffer : GpuResource
{
    private HashSet<GpuResource> _resources;
    private bool _submitted;
    private List<ShaderDataTransferState> _shaderDataTransfers;

    internal CommandBuffer(WgpuDevice owner, WGPUCommandBufferImpl* handle, HashSet<GpuResource> resources, List<ShaderDataTransferState> transfers)
        : base(owner)
    {
        Handle = handle;
        _resources = resources;
        _shaderDataTransfers = transfers;
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

    internal List<ShaderDataTransferState> TakeShaderDataTransferStates()
    {
        List<ShaderDataTransferState> transfers = _shaderDataTransfers;
        _shaderDataTransfers = [];
        return transfers;
    }

    protected override void ReleaseNative()
    {
        foreach (ShaderDataTransferState t in _shaderDataTransfers)
        {
            t.Region.Buffer.CancelShaderData(t.Region);
        }

        _shaderDataTransfers.Clear();
        WGPU.wgpuCommandBufferRelease(Handle);
        foreach (GpuResource resource in _resources)
        {
            resource.ReleaseLease();
        }

        _resources.Clear();
    }
}
