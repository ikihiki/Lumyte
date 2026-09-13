using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private RenderViewRecord RequireRenderView(NativeGpuRenderViewHandle view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view is not RenderViewRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Render view belongs to another backend.", nameof(view));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, view);
        RequireCommandTexture(record.Texture);
        return record;
    }

    internal static (AttachmentLoadOp Load, AttachmentStoreOp Store) RenderingAspectOperations(
        bool present, bool readOnly, NativeGpuLoadOp? load, NativeGpuStoreOp? store)
    {
        if (!present || readOnly)
        {
            if (load.HasValue || store.HasValue)
            {
                throw new ArgumentException("An absent or read-only aspect cannot specify load/store operations.", nameof(load));
            }
            return (AttachmentLoadOp.Load, AttachmentStoreOp.None);
        }
        if (!load.HasValue || !store.HasValue)
        {
            throw new ArgumentException("A writable aspect requires both load and store operations.", nameof(load));
        }
        return (RasterLoad(load.Value), RasterStore(store.Value));
    }

    private sealed partial class CommandRecord
    {
        public override void BeginRendering(ReadOnlySpan<NativeGpuColorAttachment> colorAttachments, NativeGpuDepthStencilAttachment? depthStencilAttachment = null)
        {
            VerifyOutsideRendering();
            if (colorAttachments.IsEmpty && !depthStencilAttachment.HasValue)
            {
                throw new ArgumentException("At least one attachment is required to define the rendering extent.", nameof(colorAttachments));
            }
            // Resolve all borrowed identities and conversions before changing native recording state.
            RenderViewRecord[] views = new RenderViewRecord[colorAttachments.Length];
            RenderingAttachmentInfo[] colors = new RenderingAttachmentInfo[views.Length];
            uint width = 0, height = 0, layers = 0;
            for (int i = 0; i < views.Length; i++)
            {
                NativeGpuColorAttachment attachment = colorAttachments[i];
                RenderViewRecord view = Owner.RequireRenderView(attachment.View);
                views[i] = view;
                if (i == 0) { AttachmentExtent(view, out width, out height, out layers); }
                colors[i] = new()
                {
                    SType = StructureType.RenderingAttachmentInfo, ImageView = view.ImageView, ImageLayout = ImageLayout.General,
                    LoadOp = RasterLoad(attachment.LoadOp), StoreOp = RasterStore(attachment.StoreOp),
                    ClearValue = new() { Color = new(attachment.ClearColor.Red, attachment.ClearColor.Green, attachment.ClearColor.Blue, attachment.ClearColor.Alpha) },
                };
            }
            RenderViewRecord? depthView = null;
            RenderingAttachmentInfo depth = default, stencil = default;
            bool hasDepth = false, hasStencil = false;
            if (depthStencilAttachment is { } depthAttachment)
            {
                depthView = Owner.RequireRenderView(depthAttachment.View);
                hasDepth = depthView.View.Aspect is NativeGpuTextureAspect.Depth or NativeGpuTextureAspect.DepthStencil;
                hasStencil = depthView.View.Aspect is NativeGpuTextureAspect.Stencil or NativeGpuTextureAspect.DepthStencil;
                if (!hasDepth && !hasStencil)
                {
                    throw new ArgumentException("A depth/stencil attachment requires a depth or stencil render view.", nameof(depthStencilAttachment));
                }
                var depthOps = RenderingAspectOperations(hasDepth, depthAttachment.DepthReadOnly, depthAttachment.DepthLoadOp, depthAttachment.DepthStoreOp);
                var stencilOps = RenderingAspectOperations(hasStencil, depthAttachment.StencilReadOnly, depthAttachment.StencilLoadOp, depthAttachment.StencilStoreOp);
                if (views.Length == 0) { AttachmentExtent(depthView, out width, out height, out layers); }
                depth = new()
                {
                    SType = StructureType.RenderingAttachmentInfo, ImageView = depthView.ImageView, ImageLayout = ImageLayout.General,
                    LoadOp = depthOps.Load, StoreOp = depthOps.Store,
                    ClearValue = new() { DepthStencil = new(depthAttachment.ClearDepth, depthAttachment.ClearStencil) },
                };
                stencil = depth;
                stencil.LoadOp = stencilOps.Load;
                stencil.StoreOp = stencilOps.Store;
            }
            // Touch may split native primary buffers. Every attachment must be prepared before
            // vkCmdBeginRendering, so neither initialization nor a split can occur inside its scope.
            foreach (RenderViewRecord view in views) { Touch(view.Texture); }
            if (depthView is not null) { Touch(depthView.Texture); }
            fixed (RenderingAttachmentInfo* nativeColors = colors)
            {
                RenderingInfo info = new()
                {
                    SType = StructureType.RenderingInfo,
                    RenderArea = new(default, new(width, height)), LayerCount = layers,
                    ColorAttachmentCount = checked((uint)colors.Length), PColorAttachments = nativeColors,
                    PDepthAttachment = hasDepth ? &depth : null, PStencilAttachment = hasStencil ? &stencil : null,
                };
                Owner.vk.CmdBeginRendering(Current, &info);
            }
            rendering = true;
            SetViewport(new(0, 0, width, height));
            SetScissor(new(0, 0, width, height));
            SetDepthStencilState(default);
        }

        public override void EndRendering()
        {
            VerifyInsideRendering();
            Owner.vk.CmdEndRendering(Current);
            rendering = false;
        }

        private static void AttachmentExtent(RenderViewRecord view, out uint width, out uint height, out uint layers)
        {
            int mip = checked((int)Math.Min(view.View.BaseMip, 31));
            width = Math.Max(1u, view.Texture.Description.Width >> mip);
            height = Math.Max(1u, view.Texture.Description.Height >> mip);
            layers = view.View.LayerCount;
        }
    }
}
