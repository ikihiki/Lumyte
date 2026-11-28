using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Explicit color and optional depth/stencil attachments, captured at declaration.</summary>
public sealed record RasterAttachments
{
    /// <summary>Gets the ordered colors; an empty list supports depth-only rendering.</summary>
    public IReadOnlyList<RasterColorAttachment> Colors { get; init; } = [];

    /// <summary>Gets the optional depth/stencil attachment.</summary>
    public RasterDepthStencilAttachment? DepthStencil { get; init; }

    internal RasterAttachments Capture() => this with { Colors = Array.AsReadOnly(Colors.ToArray()) };

    internal RenderPassDesc Prepare(PreparedResources resources) => new()
    {
        ColorAttachments = Colors.Select(c => new RenderColorAttachmentDesc
        {
            View = resources.Texture(c.Texture),
            LoadOp = c.Load,
            StoreOp = c.Store,
            ClearValue = c.Clear,
        }).ToArray(),
        DepthStencilAttachment = DepthStencil is { } d
            ? new()
            {
                View = resources.Texture(d.Texture),
                DepthLoadOp = d.DepthLoad,
                DepthStoreOp = d.DepthStore,
                DepthClearValue = d.DepthClear,
                StencilLoadOp = d.StencilLoad,
                StencilStoreOp = d.StencilStore,
                StencilClearValue = d.StencilClear,
            }
            : null,
    };
}
