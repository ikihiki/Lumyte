using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides friend-only eager target preparation without changing logical graph identity.</summary>
public sealed partial class RenderGraph
{
    internal PreparedTarget PrepareTarget(RenderGraphTexture resource)
    {
        EnsureBuilding();
        CheckResource(resource);
        IGraphicDevice device = Device ?? throw new InvalidOperationException("Target preparation requires a device.");
        ValidateAppend(device, Name);
        if (!_resources.Contains(resource) || resource.LocalPassOwner is not null)
        {
            throw new ArgumentException("Target must be a registered whole graph texture.", nameof(resource));
        }

        resource.Validate(TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        if (resource.Allocation is IGraphicsTexture imported)
        {
            PreparedTarget.ValidateTexture(imported, device);
        }

        if (resource.PreparedTarget is not { } target)
        {
            target = new PreparedTarget(resource, device);

            // Publish the owner before calling any fallible backend allocation/view factory.
            Retain(target);
            resource.PreparedTarget = target;
        }

        target.Prepare();
        return target;
    }

    internal sealed class PreparedTarget(RenderGraphTexture resource, IGraphicDevice device) : IRenderGraphLifetime
    {
        private IGraphicsTexture? _texture;
        private IGraphicsTextureView? _view;
        private int _uses;
        private bool _released;

        internal IGraphicsTexture Texture => _texture ?? throw new InvalidOperationException("Target has not been materialized.");

        internal IGraphicsTextureView View
        {
            get
            {
                ObjectDisposedException.ThrowIf(_released && _uses == 0, this);
                Validate();
                return _view ?? throw new InvalidOperationException("Target has not been prepared.");
            }
        }

        public void Validate()
        {
            ObjectDisposedException.ThrowIf(_released && _uses == 0, this);
            if (_texture is { } texture)
            {
                ValidateTexture(texture, device);
            }

            if (_view is { } view)
            {
                ValidateOrigin(view, device);
                TextureViewInfo info = view.Info;
                if (!ReferenceEquals(view.Texture, _texture) ||
                    !Equals(((IGraphicsNativeOrigin)view).AllocationIdentity, ((IGraphicsNativeOrigin)Texture).AllocationIdentity) ||
                    info.Format != Texture.Format || info.Dimension != TextureViewDimension.D2 ||
                    info.BaseMipLevel != 0 || info.MipLevelCount != 1 || info.BaseArrayLayer != 0 || info.ArrayLayerCount != 1)
                {
                    throw new ArgumentException("Target view must cover the same single mip/layer color allocation.");
                }
            }
        }

        public void Dispose()
        {
            _released = true;
            Cleanup();
        }

        internal static void ValidateTexture(IGraphicsTexture texture, IGraphicDevice device)
        {
            ValidateOrigin(texture, device);
            if (texture.Width == 0 || texture.Height == 0 || texture.MipLevels != 1 || texture.ArrayLayers != 1 ||
                (texture.Usage & TextureUsage.RenderAttachment) == 0 || texture.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8)
            {
                throw new ArgumentException("Target requires one mip and layer of a nonempty color attachment.");
            }
        }

        internal void Prepare()
        {
            ObjectDisposedException.ThrowIf(_released, this);
            _texture ??= (IGraphicsTexture)(resource.Allocation ?? resource.Create(device));
            ValidateTexture(_texture, device);
            _view ??= _texture.CreateView(new() { Dimension = TextureViewDimension.D2, MipLevelCount = 1, ArrayLayerCount = 1 });
            Validate();
        }

        internal IDisposable AcquireUse()
        {
            _ = View;
            var use = new TargetUse(this);
            _uses = checked(_uses + 1);
            return use;
        }

        private static void ValidateOrigin(object allocation, IGraphicDevice device)
        {
            if (allocation is not IGraphicsNativeOrigin origin)
            {
                throw new NotSupportedException("Target preparation requires actual native origin metadata.");
            }

            origin.ValidateOrigin();
            if (!ReferenceEquals(origin.Device, device) || device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new ArgumentException("Target must belong to the same live native device.");
            }
        }

        private void Cleanup()
        {
            if (!_released || _uses != 0)
            {
                return;
            }

            // Clear only after successful destruction: graph/plan or last use can retry failures.
            _view?.Dispose();
            _view = null;
            if (resource.IsTransient)
            {
                _texture?.Dispose();
            }

            _texture = null;
        }

        private sealed class TargetUse(PreparedTarget owner) : IDisposable
        {
            private bool _returned;

            public void Dispose()
            {
                if (!_returned)
                {
                    owner._uses--;
                    _returned = true;
                }

                owner.Cleanup();
            }
        }
    }
}
