using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>A manager-owned shader tied to the exact device epoch used to create it.</summary>
public sealed class GraphicsShaderResource
{
    internal GraphicsShaderResource(IGraphicsShader shader, IGraphicDevice device, ulong epoch)
    {
        Shader = shader;
        Device = device;
        Epoch = epoch;
    }

    /// <summary>Gets the borrowed shader, retained by the resource lease.</summary>
    public IGraphicsShader Shader { get; }

    /// <summary>Gets the original device; do not replace it during cleanup.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the original physical epoch.</summary>
    public ulong Epoch { get; }
}
