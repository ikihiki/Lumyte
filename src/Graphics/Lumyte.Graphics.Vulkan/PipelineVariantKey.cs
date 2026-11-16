using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

// Formats use four-bit enum values, with count and optional depth presence in separate lanes.
internal readonly record struct PipelineVariantKey(RenderStateKey State, ulong Formats, DescriptorLayoutKey Layout)
{
    internal static PipelineVariantKey Create(in RenderStateKey state, ReadOnlySpan<TextureFormat> formats, TextureFormat? depth, DescriptorLayoutKey layout)
    {
        if (formats.Length > RenderStateKey.MaxColorTargets)
        {
            throw new NotSupportedException("Vulkan supports at most eight color attachments.");
        }

        ulong packed = (ulong)formats.Length << 32;
        for (int i = 0; i < formats.Length; i++)
        {
            packed |= (ulong)RenderStateKey.Pack((uint)formats[i], 4) << (i * 4);
        }

        if (depth is { } value)
        {
            packed |= ((ulong)RenderStateKey.Pack((uint)value, 4) | 16ul) << 36;
        }

        return new(state, packed, layout);
    }
}
