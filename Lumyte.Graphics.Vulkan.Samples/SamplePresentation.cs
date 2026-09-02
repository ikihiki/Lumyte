namespace Lumyte.Graphics.Vulkan.Samples;

internal enum SampleKind
{
    Clear,
    RainbowTriangle,
    GeneratedTexture,
    RenderGraphLighting,
}

internal static class SamplePresentation
{
    private static readonly string[] Names =
    [
        "Clear only",
        "Vertex-color rainbow triangle",
        "Generated texture quad",
        "Render graph lit cube",
    ];

    internal static SampleKind Next(SampleKind current)
        => (SampleKind)(((int)current + 1) % Names.Length);

    internal static string Title(SampleKind current)
        => $"Lumyte Vulkan Samples | {(int)current + 1}/{Names.Length}: {Names[(int)current]} | Enter: next | Esc: exit";
}
