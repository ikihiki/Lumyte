namespace Lumyte.Graphics.RenderGraph;

/// <summary>Retains and validates external ownership until a compiled plan is released after GPU completion.</summary>
public interface IRenderGraphLifetime : IDisposable
{
    /// <summary>Rejects expired ownership before recording commands.</summary>
    void Validate();
}
