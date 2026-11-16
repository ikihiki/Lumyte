using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

// Expected: CS0453 at all public root, typed-buffer and graph boundaries.
internal class ManualClass : IShaderArguments
{
    public static string ShaderTypeName => "Rejected";
    public static int TransferSize => 4;

    public static int ReferenceCount => 0;
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct { }
}

internal static class Consumers
{
    internal static void Reject(IGraphicDevice device, IComputeEncoder encoder, global::Lumyte.Graphics.RenderGraph.RenderGraph graph, ShaderArtifact artifact)
    {
        ManualClass value = new();
        encoder.SetArguments(in value);
        device.CreateShaderDataBuffer<ManualClass>(artifact, 1);
        graph.CreateShaderDataBuffer<ManualClass>(artifact, 1);
    }
}
