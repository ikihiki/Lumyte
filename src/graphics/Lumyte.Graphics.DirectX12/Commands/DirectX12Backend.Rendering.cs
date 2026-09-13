using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.Maths;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private RenderViewRecord RequireRenderView(NativeGpuRenderViewHandle view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view is not RenderViewRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The render view belongs to another device.", nameof(view));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, view);
        RequireTexture(record.View.Texture);
        return record;
    }

    private sealed partial class NativeRecording
    {
        private bool rendering;
        private NativeGpuRasterPipelineHandle? rasterPipeline;
        private NativeGpuDepthStencilState depthStencil;

        private void VerifyOutsideRendering()
        {
            VerifyRecording();
            if (rendering) { throw new InvalidOperationException("This operation requires rendering to be ended."); }
        }

        private void VerifyInsideRendering()
        {
            VerifyRecording();
            if (!rendering) { throw new InvalidOperationException("This operation requires an active rendering section."); }
        }

        public override void BeginRendering(ReadOnlySpan<NativeGpuColorAttachment> colorAttachments,
            NativeGpuDepthStencilAttachment? depthStencilAttachment = null)
        {
            VerifyOutsideRendering();
            NativeGpuColorAttachment[] colors = colorAttachments.ToArray();
            foreach (NativeGpuColorAttachment color in colors)
            {
                RenderViewRecord view = Owner.Owner.RequireRenderView(color.View);
                if (view.View.Aspect != NativeGpuTextureAspect.Color)
                { throw new ArgumentException("A color attachment requires a color render view.", nameof(colorAttachments)); }
            }
            if (depthStencilAttachment is { } depth)
            {
                Owner.Owner.RequireRenderView(depth.View);
                Owner.Owner.ValidateDepthAttachmentOperations(depth);
            }
            NativeGpuRenderViewHandle first = colors.Length != 0 ? colors[0].View
                : depthStencilAttachment?.View ?? throw new ArgumentException("An attachment is required to define the rendering extent.", nameof(colorAttachments));
            RenderViewRecord firstView = Owner.Owner.RequireRenderView(first);
            NativeGpuTextureDescription texture = Owner.Owner.RequireTexture(firstView.View.Texture).Description;
            uint mip = firstView.View.BaseMip;
            uint width = mip >= 32 ? 1 : Math.Max(1, texture.Width >> (int)mip);
            uint height = mip >= 32 ? 1 : Math.Max(1, texture.Height >> (int)mip);
            var viewport = new Viewport(0, 0, width, height, 0, 1);
            var scissor = new Box2D<int>(0, 0, checked((int)width), checked((int)height));
            operations.Add(commands =>
            {
                Owner.Owner.EncodeBeginRendering(commands, colors, depthStencilAttachment);
                commands.RSSetViewports(1, in viewport);
                commands.RSSetScissorRects(1, in scissor);
            });
            depthStencil = default;
            rendering = true;
        }

        public override void EndRendering()
        {
            VerifyInsideRendering();
            operations.Add(commands => commands.EndRenderPass());
            rendering = false;
        }

        public override void SetPipeline(NativeGpuRasterPipelineHandle pipeline)
        {
            VerifyRecording();
            Owner.Owner.RequireRasterPipeline(pipeline);
            rasterPipeline = pipeline;
        }

        public override void SetDepthStencilState(NativeGpuDepthStencilState state)
        {
            VerifyRecording();
            depthStencil = state;
        }

        public override void SetViewport(NativeGpuViewport viewport)
        {
            VerifyRecording();
            var value = new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            operations.Add(commands => commands.RSSetViewports(1, in value));
        }

        public override void SetScissor(NativeGpuScissorRect scissor)
        {
            VerifyRecording();
            var value = new Box2D<int>(scissor.X, scissor.Y, checked((int)(scissor.X + (long)scissor.Width)),
                checked((int)(scissor.Y + (long)scissor.Height)));
            operations.Add(commands => commands.RSSetScissorRects(1, in value));
        }
    }

    private void ValidateDepthAttachmentOperations(NativeGpuDepthStencilAttachment attachment)
    {
        RenderViewRecord view = RequireRenderView(attachment.View);
        bool hasDepth = view.View.Aspect is NativeGpuTextureAspect.Depth or NativeGpuTextureAspect.DepthStencil;
        bool hasStencil = view.View.Aspect is NativeGpuTextureAspect.Stencil or NativeGpuTextureAspect.DepthStencil;
        if (!hasDepth && !hasStencil) { throw new ArgumentException("A depth/stencil attachment requires a depth/stencil view.", nameof(attachment)); }
        ValidateAspectOperations(hasDepth, attachment.DepthReadOnly, attachment.DepthLoadOp, attachment.DepthStoreOp);
        ValidateAspectOperations(hasStencil, attachment.StencilReadOnly, attachment.StencilLoadOp, attachment.StencilStoreOp);

        static void ValidateAspectOperations(bool present, bool readOnly, NativeGpuLoadOp? load, NativeGpuStoreOp? store)
        {
            if (!present || readOnly)
            {
                if (load.HasValue || store.HasValue)
                { throw new ArgumentException("Read-only or absent aspects cannot specify load/store operations.", "attachment"); }
            }
            else if (!load.HasValue || !store.HasValue)
            { throw new ArgumentException("Writable aspects must specify both load and store operations.", "attachment"); }
        }
    }

    private void EncodeBeginRendering(ComPtr<ID3D12GraphicsCommandList8> commands,
        NativeGpuColorAttachment[] colors, NativeGpuDepthStencilAttachment? depth)
    {
        var native = new RenderPassRenderTargetDesc[colors.Length];
        for (int index = 0; index < colors.Length; index++)
        {
            NativeGpuColorAttachment color = colors[index];
            RenderViewRecord view = RequireRenderView(color.View);
            ClearValue clear = default;
            clear.Format = TextureFormat(view.View.Format, false);
            clear.Anonymous.Color[0] = color.ClearColor.Red; clear.Anonymous.Color[1] = color.ClearColor.Green;
            clear.Anonymous.Color[2] = color.ClearColor.Blue; clear.Anonymous.Color[3] = color.ClearColor.Alpha;
            native[index] = new(view.Heap.GetCPUDescriptorHandleForHeapStart(),
                BeginningAccess(color.LoadOp, clear), EndingAccess(color.StoreOp));
        }
        RenderPassDepthStencilDesc depthNative = default;
        RenderPassFlags flags = RenderPassFlags.AllowUavWrites;
        if (depth is { } attachment)
        {
            RenderViewRecord view = RequireRenderView(attachment.View);
            ValidateDepthAttachmentOperations(attachment);
            ClearValue clear = default;
            clear.Format = TextureFormat(view.View.Format, false);
            clear.DepthStencil = new(attachment.ClearDepth, attachment.ClearStencil);
            bool hasDepth = view.View.Aspect is NativeGpuTextureAspect.Depth or NativeGpuTextureAspect.DepthStencil;
            bool hasStencil = view.View.Aspect is NativeGpuTextureAspect.Stencil or NativeGpuTextureAspect.DepthStencil;
            depthNative = new(view.Heap.GetCPUDescriptorHandleForHeapStart(),
                AspectBeginning(hasDepth, attachment.DepthReadOnly, attachment.DepthLoadOp, clear),
                AspectBeginning(hasStencil, attachment.StencilReadOnly, attachment.StencilLoadOp, clear),
                AspectEnding(hasDepth, attachment.DepthReadOnly, attachment.DepthStoreOp),
                AspectEnding(hasStencil, attachment.StencilReadOnly, attachment.StencilStoreOp));
            if (attachment.DepthReadOnly) { flags |= RenderPassFlags.BindReadOnlyDepth; }
            if (attachment.StencilReadOnly) { flags |= RenderPassFlags.BindReadOnlyStencil; }
        }
        fixed (RenderPassRenderTargetDesc* pointer = native)
        {
            commands.BeginRenderPass(checked((uint)native.Length), pointer, depth.HasValue ? &depthNative : null, flags);
        }
    }

    private static RenderPassBeginningAccess BeginningAccess(NativeGpuLoadOp operation, ClearValue clear) => new()
    {
        Type = operation switch
        {
            NativeGpuLoadOp.Load => RenderPassBeginningAccessType.Preserve,
            NativeGpuLoadOp.Clear => RenderPassBeginningAccessType.Clear,
            NativeGpuLoadOp.Discard => RenderPassBeginningAccessType.Discard,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        },
        Clear = new(clear),
    };

    private static RenderPassEndingAccess EndingAccess(NativeGpuStoreOp operation) => new()
    {
        Type = operation switch
        {
            NativeGpuStoreOp.Store => RenderPassEndingAccessType.Preserve,
            NativeGpuStoreOp.Discard => RenderPassEndingAccessType.Discard,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        },
    };

    private static RenderPassBeginningAccess AspectBeginning(bool present, bool readOnly, NativeGpuLoadOp? operation, ClearValue clear)
        => !present ? new() { Type = RenderPassBeginningAccessType.NoAccess }
            : BeginningAccess(readOnly ? NativeGpuLoadOp.Load : operation!.Value, clear);

    private static RenderPassEndingAccess AspectEnding(bool present, bool readOnly, NativeGpuStoreOp? operation)
        => !present ? new() { Type = RenderPassEndingAccessType.NoAccess }
            : EndingAccess(readOnly ? NativeGpuStoreOp.Store : operation!.Value);
}
