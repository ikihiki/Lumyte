using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides the command buffer and declared resources during one pass callback.</summary>
public sealed class RenderGraphContext
{
    private readonly IGraphicsCommandBuffer _commands;
    private readonly IReadOnlyList<RenderGraphUse> _uses;
    private readonly CompiledRenderGraph _plan;
    private IDisposable? _encoder;
    private Exception? _encoderCleanupError;
    private bool _active = true;

    internal RenderGraphContext(IGraphicsCommandBuffer commands, IReadOnlyList<RenderGraphUse> uses, CompiledRenderGraph plan, RenderGraphPass pass)
    {
        _commands = commands;
        _uses = uses;
        _plan = plan;
        Pass = pass;
    }

    /// <summary>Gets the caller-owned recording; do not mix raw passes with an active graph encoder.</summary>
    public IGraphicsCommandBuffer Commands
    {
        get
        {
            EnsureActive();
            return _commands;
        }
    }

    internal RenderGraphPass Pass { get; }

    internal ShaderTarget? DeviceTarget => Pass.Owner.Device?.Caps.ShaderTarget;

    /// <summary>Resolves a predeclared logical image view to its existing native shader reference.</summary>
    /// <param name="resources">The resource group declared for this pass.</param>
    /// <param name="texture">The registered logical image.</param>
    /// <param name="desc">The exact registered view description.</param>
    /// <returns>The materialized reference; no registration or table mutation occurs.</returns>
    public IGpuRef<IGraphicsTextureView> GetGpuRef(RenderGraphShaderResources resources, RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Resolve(this, texture, desc);
    }

    /// <summary>Resolves a predeclared graph-owned sampler.</summary>
    /// <param name="resources">The resource group declared for this pass.</param>
    /// <param name="desc">The registered sampler description.</param>
    /// <returns>The materialized sampler reference.</returns>
    public IGpuRef<IGraphicsSampler> GetGpuRef(RenderGraphShaderResources resources, SamplerDesc desc)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Resolve(this, desc);
    }

    /// <summary>Resolves a predeclared borrowed sampler by identity.</summary>
    /// <param name="resources">The resource group declared for this pass.</param>
    /// <param name="sampler">The registered sampler object.</param>
    /// <returns>The materialized sampler reference.</returns>
    public IGpuRef<IGraphicsSampler> GetGpuRef(RenderGraphShaderResources resources, IGraphicsSampler sampler)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Resolve(this, sampler);
    }

    /// <summary>Resolves an exact predeclared typed logical buffer range.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="resources">The resource group declared for this pass.</param>
    /// <param name="buffer">The registered logical buffer.</param>
    /// <param name="offset">The registered element offset.</param>
    /// <param name="count">The registered element count.</param>
    /// <param name="access">The registered shader access.</param>
    /// <returns>The materialized buffer reference, supporting ordinary GetElement operations.</returns>
    public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderResources resources, RenderGraphBuffer<T> buffer, ulong offset, ulong count, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Resolve(this, buffer, offset, count, access);
    }

    /// <summary>Resolves an exact predeclared logical shader-data range.</summary>
    /// <typeparam name="T">The shader-data element type.</typeparam>
    /// <param name="resources">The resource group declared for this pass.</param>
    /// <param name="buffer">The registered logical shader-data buffer.</param>
    /// <param name="offset">The registered element offset.</param>
    /// <param name="count">The registered element count.</param>
    /// <returns>The materialized shader-data reference.</returns>
    public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderResources resources, RenderGraphShaderDataBuffer<T> buffer, ulong offset, ulong count)
        where T : struct, IShaderData
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Resolve(this, buffer, offset, count);
    }

    /// <summary>Resolves a texture declared by this pass.</summary>
    /// <param name="texture">The imported handle.</param>
    /// <returns>The caller-owned texture.</returns>
    public IGraphicsTexture GetTexture(RenderGraphTexture texture)
    {
        Check(texture);
        return (IGraphicsTexture)_plan.Resolve(texture);
    }

    /// <summary>Resolves a typed buffer declared by this pass.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="buffer">The imported handle.</param>
    /// <returns>The caller-owned buffer.</returns>
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer)
        where T : unmanaged
    {
        Check(buffer);
        return (IGraphicsBuffer<T>)_plan.Resolve(buffer);
    }

    /// <summary>Resolves shader data declared by this pass.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="buffer">The graph-local handle.</param>
    /// <returns>The caller-owned shader data buffer.</returns>
    public IGraphicsShaderDataBuffer<T> GetShaderDataBuffer<T>(RenderGraphShaderDataBuffer<T> buffer)
        where T : struct, IShaderData
    {
        Check(buffer);
        return (IGraphicsShaderDataBuffer<T>)_plan.Resolve(buffer);
    }

    /// <summary>Creates a plan-owned texture view retained until GPU completion and plan disposal.</summary>
    /// <param name="texture">A texture declared by this pass.</param>
    /// <param name="desc">The requested subresources, or the backend default.</param>
    /// <returns>A view borrowed from the plan; do not dispose it directly.</returns>
    public IGraphicsTextureView CreateTextureView(RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        Check(texture);
        return _plan.CreateView(texture, desc);
    }

    /// <summary>Begins a render scope using declared logical attachments and plan-owned views.</summary>
    /// <param name="desc">The logical attachments and load/store operations.</param>
    /// <returns>A callback-scoped graph render encoder.</returns>
    public RenderGraphRenderEncoder BeginRenderPass(RenderGraphRenderPassDesc desc)
    {
        EnsureCanBegin();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.ColorAttachments);
        RenderGraphColorAttachmentDesc[] colors = desc.ColorAttachments.ToArray();
        RenderGraphDepthStencilAttachmentDesc? depth = desc.DepthStencilAttachment;
        if ((colors.Length == 0 && depth is null) ||
            (Pass.Owner.Device is { } device && (uint)colors.Length > device.Caps.MaxColorAttachments))
        {
            throw new ArgumentException("Invalid graph render attachment count.", nameof(desc));
        }

        foreach (RenderGraphColorAttachmentDesc color in colors)
        {
            ArgumentNullException.ThrowIfNull(color);
            ResourceAccess access = ResourceAccess.ColorWrite;
            if (color.LoadOp == AttachmentLoadOp.Load)
            {
                access |= ResourceAccess.ColorRead;
            }

            CheckAttachment(color.Texture, TextureState.ColorAttachment, PipelineStage.ColorOutput, access);
        }

        if (depth is not null)
        {
            Check(depth.Texture);
            bool stencil = GetTexture(depth.Texture).Format == TextureFormat.Depth24Stencil8;
            ResourceAccess access = ResourceAccess.DepthStencilWrite;
            if (depth.DepthLoadOp == AttachmentLoadOp.Load || (stencil && depth.StencilLoadOp == AttachmentLoadOp.Load))
            {
                access |= ResourceAccess.DepthStencilRead;
            }

            CheckAttachment(depth.Texture, TextureState.DepthStencilAttachment, PipelineStage.DepthStencil, access);
        }

        var nativeDesc = new RenderPassDesc
        {
            Label = desc.Label,
            ColorAttachments = colors.Select(color => new RenderColorAttachmentDesc
            {
                View = CreateTextureView(color.Texture, color.ViewDesc),
                LoadOp = color.LoadOp,
                StoreOp = color.StoreOp,
                ClearValue = color.ClearValue,
            }).ToArray(),
            DepthStencilAttachment = depth is null ? null : new RenderDepthStencilAttachmentDesc
            {
                View = CreateTextureView(depth.Texture, depth.ViewDesc),
                DepthLoadOp = depth.DepthLoadOp,
                DepthStoreOp = depth.DepthStoreOp,
                DepthClearValue = depth.DepthClearValue,
                StencilLoadOp = depth.StencilLoadOp,
                StencilStoreOp = depth.StencilStoreOp,
                StencilClearValue = depth.StencilClearValue,
            },
        };
        var encoder = new RenderGraphRenderEncoder(this, _commands.BeginRenderPass(nativeDesc), colors.Select(color => color.Texture).ToArray(), depth?.Texture);
        _encoder = encoder;
        return encoder;
    }

    /// <summary>Begins a callback-scoped graph compute encoder.</summary>
    /// <param name="label">An optional diagnostic label.</param>
    /// <returns>The graph compute scope.</returns>
    public RenderGraphComputeEncoder BeginComputePass(string? label = null)
    {
        EnsureCanBegin();
        var encoder = new RenderGraphComputeEncoder(this, _commands.BeginComputePass(new ComputePassDesc { Label = label }));
        _encoder = encoder;
        return encoder;
    }

    internal void EncoderEnded(IDisposable encoder)
    {
        if (ReferenceEquals(_encoder, encoder))
        {
            _encoder = null;
        }
    }

    internal void DeferEncoderCleanup(Exception error) => _encoderCleanupError ??= error;

    internal void Close(Exception? callbackError = null)
    {
        if (!_active)
        {
            return;
        }

        try
        {
            _encoder?.Dispose();
            if (_encoderCleanupError is { } cleanupError)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
            }
        }
        catch (Exception cleanupError) when (callbackError is not null)
        {
            // Retain the original exception and make the secondary failure inspectable.
            try
            {
                callbackError.Data["RenderGraph.EncoderCleanupException"] = cleanupError;
            }
            catch (Exception)
            {
                // Even an exception with non-writable diagnostic data must remain the primary failure.
            }
        }
        finally
        {
            _encoder = null;
            _active = false;
        }
    }

    internal (IReadOnlyList<ShaderResources>? Resources, ShaderValueSnapshot Snapshot) SelectArguments(ShaderValueSnapshot snapshot, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        EnsureActive();
        if (!snapshot.Values.Any(value => value.IsReference))
        {
            return (null, snapshot);
        }

        GraphShaderResourceOwner owner = Pass.ShaderResourcesOwner ?? throw new ArgumentException("Declare a resource group for this pass before resolving root references.");
        IReadOnlyList<ShaderResources> resources = owner.SelectArguments(snapshot, Pass, outputs, graphics);
        return (resources, owner.Constrain(resources, snapshot));
    }

    internal void BindResources(IReadOnlyList<ShaderResources> resources, IRenderEncoder encoder, IReadOnlyList<RenderGraphTexture> outputs)
    {
        EnsureActive();
        GraphShaderResourceOwner owner = Pass.ShaderResourcesOwner!;
        foreach (ShaderResources candidate in resources)
        {
            owner.ValidateExecution(candidate, Pass, outputs, graphics: true);
            owner.ValidateLookup(candidate, Pass);
        }

        owner.Bind(resources[0], encoder);
    }

    internal void BindResources(IReadOnlyList<ShaderResources> resources, IComputeEncoder encoder)
    {
        EnsureActive();
        GraphShaderResourceOwner owner = Pass.ShaderResourcesOwner!;
        foreach (ShaderResources candidate in resources)
        {
            owner.ValidateExecution(candidate, Pass, [], graphics: false);
            owner.ValidateLookup(candidate, Pass);
        }

        owner.Bind(resources[0], encoder);
    }

    internal void EnsureActive()
    {
        if (!_active || _encoderCleanupError is not null)
        {
            throw new InvalidOperationException("Pass context is closed or its encoder cleanup failed.");
        }
    }

    private void EnsureCanBegin()
    {
        EnsureActive();
        if (_encoder is not null)
        {
            throw new InvalidOperationException("Only one graph encoder can be active in a pass context.");
        }
    }

    private void CheckAttachment(RenderGraphTexture texture, TextureState state, PipelineStage stages, ResourceAccess access)
    {
        Check(texture);
        if (!_uses.Any(use => use.Resource == texture && use.State == state &&
            (use.Scope.Stages & stages) == stages && (use.Scope.Access & access) == access))
        {
            throw new InvalidOperationException("Attachment state and actual access must be declared by this pass.");
        }
    }

    private void Check(RenderGraphResource resource)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resource);
        if (!_uses.Any(use => use.Resource == resource))
        {
            throw new ArgumentException("Resource was not declared by this pass.", nameof(resource));
        }
    }
}
