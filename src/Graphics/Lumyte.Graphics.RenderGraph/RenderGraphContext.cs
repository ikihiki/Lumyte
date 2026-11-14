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

    /// <summary>Resolves the pass's predeclared sampled image view without changing its registration plan.</summary>
    /// <param name="texture">The pass-declared logical image.</param>
    /// <param name="desc">The exact declared sampled view, or its default.</param>
    /// <returns>The existing native shader reference for this pass.</returns>
    public IGpuRef<IGraphicsTextureView> GetGpuRef(RenderGraphTexture texture, TextureViewDesc? desc = null)
    {
        EnsureActive();
        return ShaderLookup().Resolve(this, texture, desc);
    }

    /// <summary>Resolves a pass-declared graph-owned sampler by value.</summary>
    /// <param name="desc">The sampler description declared with UseSampler.</param>
    /// <returns>The existing native sampler reference.</returns>
    public IGpuRef<IGraphicsSampler> GetGpuRef(SamplerDesc desc)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(desc);
        return ShaderLookup().ResolveSampler(this, desc);
    }

    /// <summary>Resolves a pass-declared borrowed sampler by identity.</summary>
    /// <param name="sampler">The sampler declared with UseSampler.</param>
    /// <returns>The existing native sampler reference.</returns>
    public IGpuRef<IGraphicsSampler> GetGpuRef(IGraphicsSampler sampler)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(sampler);
        return ShaderLookup().ResolveSampler(this, sampler);
    }

    /// <summary>Resolves an exact typed range declared for shader access on this pass.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="buffer">The pass-declared logical buffer.</param>
    /// <param name="offset">The declared element offset.</param>
    /// <param name="count">The declared element count; null requests the whole buffer.</param>
    /// <returns>The existing native range reference with declaration-derived access.</returns>
    public IGpuRef<T> GetGpuRef<T>(RenderGraphBuffer<T> buffer, ulong offset = 0, ulong? count = null)
        where T : unmanaged
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(buffer);
        return ShaderLookup().ResolveBuffer<T>(this, buffer, offset, count ?? buffer.Count);
    }

    /// <summary>Resolves an exact declared shader-data range without recapturing transferred metadata.</summary>
    /// <typeparam name="T">The shader-data element type.</typeparam>
    /// <param name="buffer">The pass-declared logical shader-data buffer.</param>
    /// <param name="offset">The declared element offset.</param>
    /// <param name="count">The declared element count; null requests the whole buffer.</param>
    /// <returns>The existing native range reference.</returns>
    public IGpuRef<T> GetGpuRef<T>(RenderGraphShaderDataBuffer<T> buffer, ulong offset = 0, ulong? count = null)
        where T : IShaderArguments
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(buffer);
        return ShaderLookup().ResolveBuffer<T>(this, buffer, offset, count ?? buffer.Count);
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
        where T : IShaderArguments
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
        if (colors.Select(color => color?.Texture).Distinct().Count() != colors.Length ||
            (colors.Length == 0 && depth is null) ||
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

    internal (IReadOnlyList<ShaderResources>? Resources, IShaderArgumentScope? Scope) SelectArguments(IShaderReferenceMetadata references, IReadOnlyList<RenderGraphTexture> outputs, bool graphics)
    {
        EnsureActive();
        if (references.References.Count == 0)
        {
            return (null, null);
        }

        GraphShaderResourceOwner owner = _plan.ShaderResourceOwner ?? throw new ArgumentException("Declare shader resources on this pass before resolving references.");
        IReadOnlyList<ShaderResources> resources = owner.SelectArguments(references, Pass, outputs, graphics);
        return (resources, owner.Scope(resources));
    }

    internal void BindResources(IReadOnlyList<ShaderResources> resources, IRenderEncoder encoder, IReadOnlyList<RenderGraphTexture> outputs)
    {
        EnsureActive();
        GraphShaderResourceOwner owner = _plan.ShaderResourceOwner!;
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
        GraphShaderResourceOwner owner = _plan.ShaderResourceOwner!;
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

    private GraphBindingLookup ShaderLookup()
        => (_plan.ShaderResourceOwner ?? throw new ArgumentException("This compiled pass has no shader declarations or device.")).Lookup(Pass);

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
