using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides the command buffer and declared resources during one pass callback.</summary>
public sealed class RenderGraphContext
{
    private readonly IGraphicsCommandBuffer _commands;
    private readonly IReadOnlyList<RenderGraphUse> _uses;
    private bool _active = true;

    internal RenderGraphContext(IGraphicsCommandBuffer commands, IReadOnlyList<RenderGraphUse> uses)
    {
        _commands = commands;
        _uses = uses;
    }

    /// <summary>Gets the caller-owned recording; callbacks must close all render and compute encoders.</summary>
    public IGraphicsCommandBuffer Commands
    {
        get
        {
            EnsureActive();
            return _commands;
        }
    }

    /// <summary>Resolves a texture declared by this pass.</summary>
    /// <param name="texture">The imported handle.</param>
    /// <returns>The caller-owned texture.</returns>
    public IGraphicsTexture GetTexture(RenderGraphTexture texture)
    {
        Check(texture);
        return texture.Texture;
    }

    /// <summary>Resolves a typed buffer declared by this pass.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="buffer">The imported handle.</param>
    /// <returns>The caller-owned buffer.</returns>
    public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer)
        where T : unmanaged
    {
        Check(buffer);
        return buffer.Buffer;
    }

    internal void Close() => _active = false;

    private void Check(RenderGraphResource resource)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(resource);
        if (!_uses.Any(use => use.Resource == resource))
        {
            throw new ArgumentException("Resource was not declared by this pass.", nameof(resource));
        }
    }

    private void EnsureActive()
    {
        if (!_active)
        {
            throw new InvalidOperationException("Pass context is only valid during its callback.");
        }
    }
}
