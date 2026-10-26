using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Tests;

internal sealed class ExternalDrawingResources : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    internal ExternalDrawingResources(IGraphicDevice device)
    {
        try
        {
            var vertexArtifact = new ShaderArtifact(DrawingShaders2D.Vertex.GetBinary());
            var fragmentArtifact = new ShaderArtifact(DrawingShaders2D.Fragment.GetBinary());
            IGraphicsShader vertex = device.CreateShader(vertexArtifact);
            _owned.Add(vertex);
            IGraphicsShader fragment = device.CreateShader(fragmentArtifact);
            _owned.Add(fragment);
            IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
            _owned.Add(pipeline);
            Resources = new(device, pipeline);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal DrawingResources2D Resources { get; }

    internal int Disposals { get; private set; }

    public void Dispose()
    {
        if (_owned.Count == 0)
        {
            return;
        }

        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }

        Disposals++;
    }
}
