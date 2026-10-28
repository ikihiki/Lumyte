namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes draw state to snapshot independently of the program.</summary>
public sealed record GraphicsRenderStateDesc
{
    /// <summary>Gets the exact primitive assembly mode.</summary>
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;

    /// <summary>Gets the indexed strip restart format.</summary>
    public IndexFormat? StripIndexFormat { get; init; }

    /// <summary>Gets the rasterization settings.</summary>
    public RasterizationStateDesc Rasterization { get; init; } = new();

    /// <summary>Gets the depth and stencil settings.</summary>
    public DepthStencilStateDesc DepthStencil { get; init; } = new();

    /// <summary>Gets one color state per pass attachment.</summary>
    public IReadOnlyList<ColorBlendStateDesc> ColorTargets { get; init; } = [];

    /// <summary>Gets the exact coverage mask including zero.</summary>
    public uint SampleMask { get; init; } = uint.MaxValue;

    /// <summary>Creates default draw state for the explicitly supplied number of color attachments.</summary>
    /// <param name="colorAttachmentCount">The nonnegative count; zero supports a depth-only pass.</param>
    /// <returns>Default state with one independent color descriptor per attachment.</returns>
    public static GraphicsRenderStateDesc ForColorTargets(int colorAttachmentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(colorAttachmentCount);
        var targets = new ColorBlendStateDesc[colorAttachmentCount];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = new();
        }

        return new() { ColorTargets = Array.AsReadOnly(targets) };
    }
}
