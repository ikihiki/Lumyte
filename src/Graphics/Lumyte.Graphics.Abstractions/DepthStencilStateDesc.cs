namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes depth and stencil tests for compatible attachments.</summary>
public sealed record DepthStencilStateDesc
{
    /// <summary>Gets a value indicating whether depth testing is active.</summary>
    public bool DepthTestEnable { get; init; }

    /// <summary>Gets a value indicating whether depth writing is active.</summary>
    public bool DepthWriteEnable { get; init; }

    /// <summary>Gets depth comparison, independent of write enable.</summary>
    public CompareFunction DepthCompare { get; init; } = CompareFunction.LessOrEqual;

    /// <summary>Gets a value indicating whether stencil testing is active.</summary>
    public bool StencilTestEnable { get; init; }

    /// <summary>Gets front stencil operations.</summary>
    public StencilFaceDesc Front { get; init; } = new();

    /// <summary>Gets back stencil operations.</summary>
    public StencilFaceDesc Back { get; init; } = new();

    /// <summary>Gets the eight bit read mask.</summary>
    public uint StencilReadMask { get; init; } = 0xff;

    /// <summary>Gets the eight bit write mask.</summary>
    public uint StencilWriteMask { get; init; } = 0xff;
}
