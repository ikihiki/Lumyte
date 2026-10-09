namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes stencil comparison and operations.</summary>
public sealed record StencilFaceDesc
{
    /// <summary>Gets the stencil comparison.</summary>
    public CompareFunction Compare { get; init; } = CompareFunction.Always;

    /// <summary>Gets the stencil test failure operation.</summary>
    public StencilOperation Fail { get; init; } = StencilOperation.Keep;

    /// <summary>Gets the depth failure operation.</summary>
    public StencilOperation DepthFail { get; init; } = StencilOperation.Keep;

    /// <summary>Gets the passing operation.</summary>
    public StencilOperation Pass { get; init; } = StencilOperation.Keep;
}
