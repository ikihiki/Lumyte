using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawRecordResources2D(IGraphicDevice device, long poolBudget) : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly Dictionary<int, IGraphicsPipeline> _pipelines = [];

    internal DrawNumericPool2D NumericPool { get; } = new(device, poolBudget);

    public void Dispose()
    {
        NumericPool.Dispose();
        while (_owned.Count != 0)
        {
            _owned[^1].Dispose();
            _owned.RemoveAt(_owned.Count - 1);
        }
    }

    internal static int SelectPipelineMode(int mode, int paintEntries) => mode == 2 && paintEntries <= 8 ? 6 : mode;

    internal IGraphicsPipeline GetPipeline(int mode)
    {
        if (_pipelines.TryGetValue(mode, out IGraphicsPipeline? existing))
        {
            return existing;
        }

        string variant = mode switch { 0 => "-basic", 1 => "-mask", 2 => "-image", 4 => "-solid", 6 => "-image-small", _ => string.Empty };
        IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-vertex.lshader")));
        IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-fragment.lshader")));
        DrawRecordAbi2D.Validate(vertex.Artifact, device.Caps.ShaderTarget, mode);
        DrawRecordAbi2D.Validate(fragment.Artifact, device.Caps.ShaderTarget, mode);
        IGraphicsPipeline pipeline = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        _pipelines.Add(mode, pipeline);
        return pipeline;
    }

    private T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }
}
