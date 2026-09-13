using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private unsafe F.RenderPassEncoderHandle BeginRenderPass(CommandRecording recording, F.CommandEncoderHandle encoder,
        BeginRenderingCommand command, HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        var colors = new F.RenderPassColorAttachmentFFI[command.Colors.Length];
        for (int index = 0; index < colors.Length; index++)
        {
            P.GpuColorAttachment color = command.Colors[index];
            if (color.DepthSlice == F.WebGPU_FFI.DEPTH_SLICE_UNDEFINED)
            { throw new ArgumentOutOfRangeException(nameof(command), "An explicit DepthSlice cannot use WebGPU's undefined sentinel."); }
            colors[index] = new()
            {
                View = AcquireAttachmentView(recording, color.View, dependencies),
                ResolveTarget = color.ResolveTarget is { } resolve ? AcquireAttachmentView(recording, resolve, dependencies) : default,
                DepthSlice = color.DepthSlice ?? F.WebGPU_FFI.DEPTH_SLICE_UNDEFINED,
                LoadOp = MapAttachmentLoad(color.LoadOperation),
                StoreOp = MapAttachmentStore(color.StoreOperation),
                ClearValue = MapClearColor(color.ClearColor),
            };
        }
        F.RenderPassDepthStencilAttachmentFFI depth = default;
        if (command.DepthStencil is { } attachment)
        {
            depth = new()
            {
                View = AcquireAttachmentView(recording, attachment.View, dependencies),
                DepthReadOnly = attachment.DepthReadOnly,
                StencilReadOnly = attachment.StencilReadOnly,
                DepthLoadOp = attachment.DepthLoadOperation is { } depthLoad ? MapAttachmentLoad(depthLoad) : N.LoadOp.Undefined,
                DepthStoreOp = attachment.DepthStoreOperation is { } depthStore ? MapAttachmentStore(depthStore) : N.StoreOp.Undefined,
                StencilLoadOp = attachment.StencilLoadOperation is { } stencilLoad ? MapAttachmentLoad(stencilLoad) : N.LoadOp.Undefined,
                StencilStoreOp = attachment.StencilStoreOperation is { } stencilStore ? MapAttachmentStore(stencilStore) : N.StoreOp.Undefined,
                DepthClearValue = attachment.ClearValue.Depth,
                StencilClearValue = attachment.ClearValue.Stencil,
            };
        }
        fixed (F.RenderPassColorAttachmentFFI* pointer = colors)
        {
            var description = new F.RenderPassDescriptorFFI
            {
                ColorAttachments = pointer,
                ColorAttachmentCount = (nuint)colors.Length,
                DepthStencilAttachment = command.DepthStencil.HasValue ? &depth : null,
            };
            F.RenderPassEncoderHandle pass = F.WebGPU_FFI.CommandEncoderBeginRenderPass(encoder, &description);
            RequireNativeObject((nuint)pass, "render pass");
            return pass;
        }
    }

    private F.TextureViewHandle AcquireAttachmentView(CommandRecording recording, P.GpuTextureView view,
        HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        TextureViewLease lease = AcquireTextureView(view, N.TextureUsage.RenderAttachment);
        recording.RetainAttachmentView(lease);
        dependencies.Add(lease.Diagnostics);
        return lease.Handle;
    }

    private static N.LoadOp MapAttachmentLoad(P.GpuAttachmentLoadOperation operation) => operation switch
    {
        P.GpuAttachmentLoadOperation.Load => N.LoadOp.Load,
        P.GpuAttachmentLoadOperation.Clear => N.LoadOp.Clear,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static N.StoreOp MapAttachmentStore(P.GpuAttachmentStoreOperation operation) => operation switch
    {
        P.GpuAttachmentStoreOperation.Store => N.StoreOp.Store,
        P.GpuAttachmentStoreOperation.Discard => N.StoreOp.Discard,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
