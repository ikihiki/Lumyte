using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawRecordResources2D : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    internal DrawRecordResources2D(IGraphicDevice device)
    {
        try
        {
            IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records-vertex.lshader")));
            IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records-fragment.lshader")));
            DrawRecordAbi2D.Validate(vertex.Artifact, device.Caps.ShaderTarget);
            DrawRecordAbi2D.Validate(fragment.Artifact, device.Caps.ShaderTarget);
            Pipeline = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IGraphicsPipeline Pipeline { get; }

    public void Dispose()
    {
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    private T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }
}
