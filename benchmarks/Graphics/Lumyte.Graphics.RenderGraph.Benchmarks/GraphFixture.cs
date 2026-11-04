using Lumyte.Graphics.Abstractions;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmarks;

internal sealed class GraphFixture
{
    private static readonly TextureDesc _description = new()
    {
        Width = 16,
        Height = 16,
        Format = TextureFormat.Rgba8Unorm,
        Usage = TextureUsage.CopySource | TextureUsage.CopyDestination,
    };

    private static readonly BarrierScope _read = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _write = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private readonly IGraphicDevice _device;
    private readonly GraphProfile _profile;
    private readonly string[] _names;
    private readonly int[][] _inputs;
    private readonly string[] _resourceNames;

    internal GraphFixture(IGraphicDevice device, GraphProfile profile)
    {
        _device = device;
        _profile = profile;
        _names = Enumerable.Range(0, profile.Passes).Select(index => $"pass-{index:D3}").ToArray();
        _inputs = Enumerable.Range(0, profile.Passes).Select(profile.Inputs).ToArray();
        _resourceNames = Enumerable.Range(0, profile.Passes * profile.Lanes).Select(index => $"texture-{index:D3}").ToArray();
    }

    internal Graph Build()
    {
        var graph = new Graph(_device, _profile.Id);
        try
        {
            var resources = new RenderGraphTexture[_profile.Passes * _profile.Lanes];
            for (int index = 0; index < resources.Length; index++)
            {
                resources[index] = graph.CreateTexture(_description, _resourceNames[index]);
            }

            for (int index = 0; index < _profile.Passes; index++)
            {
                // Never executed: this fixture measures declaration/compilation, not Record.
                RenderGraphPass pass = graph.AddPass(_names[index], static _ => throw new NotSupportedException("G04 CPU declaration fixture must not be recorded."));
                for (int lane = 0; lane < _profile.Lanes; lane++)
                {
                    foreach (int input in _inputs[index])
                    {
                        pass.UseTexture(resources[(input * _profile.Lanes) + lane], TextureState.CopySource, _read);
                    }

                    RenderGraphTexture output = resources[(index * _profile.Lanes) + lane];
                    pass.UseTexture(output, TextureState.CopyDestination, _write, discard: true);
                    if (_profile.IsOutput(index))
                    {
                        graph.MarkOutput(output);
                    }
                }
            }

            return graph;
        }
        catch
        {
            graph.Dispose();
            throw;
        }
    }
}
