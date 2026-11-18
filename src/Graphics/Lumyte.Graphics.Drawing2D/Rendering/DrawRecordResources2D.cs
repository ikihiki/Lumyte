using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawRecordResources2D(IGraphicDevice device, long poolBudget) : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly Dictionary<int, IGraphicsPipeline> _pipelines = [];
    private IGraphicsComputePipeline? _membership;

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

    internal static int SelectPipelineMode(int mode, int paintClosureBound) => mode switch
    {
        2 => paintClosureBound <= 8 ? 6 : paintClosureBound <= 16 ? 7 : 2,
        8 => paintClosureBound <= 8 ? 9 : paintClosureBound <= 16 ? 10 : 8,
        _ => mode,
    };

    internal IGraphicsPipeline GetPipeline(int mode)
    {
        if (_pipelines.TryGetValue(mode, out IGraphicsPipeline? existing))
        {
            return existing;
        }

        string variant = mode switch { 10 => "-membership-medium", 9 => "-membership-small", 8 => "-membership", 0 => "-basic", 1 => "-mask", 2 => "-image", 4 => "-solid", 6 => "-image-small", 7 => "-image-medium", _ => string.Empty };
        IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records" + (mode is 9 or 10 ? "-membership" : variant) + "-vertex.lshader")));
        IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.draw-records" + variant + "-fragment.lshader")));
        DrawRecordAbi2D.Validate(vertex.Artifact, device.Caps.ShaderTarget, mode);
        DrawRecordAbi2D.Validate(fragment.Artifact, device.Caps.ShaderTarget, mode);
        IGraphicsPipeline pipeline = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        _pipelines.Add(mode, pipeline);
        return pipeline;
    }

    internal IGraphicsComputePipeline GetMembershipPipeline()
    {
        if (_membership is not null)
        {
            return _membership;
        }

        IGraphicsShader shader = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(DrawRecordResources2D).Assembly, "Lumyte.Drawing2D.shape-membership-compute.lshader")));
        return _membership = Own(device.CreateComputePipeline(new() { ComputeShader = shader }));
    }

    private T Own<T>(T value)
        where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }
}
