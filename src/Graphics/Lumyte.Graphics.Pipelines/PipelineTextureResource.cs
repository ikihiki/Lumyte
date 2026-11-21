using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class PipelineTextureResource(IGraphicDevice device, GraphicsDeviceGeneration generation, IGraphicsTextureView view, TextureState state, BarrierScope scope)
    : PipelineExternalResource(device, generation, view.Texture, scope)
{
    private readonly object _gate = new();
    private readonly HashSet<TextureState> _states = state == TextureState.Undefined ? [] : [state];

    internal IGraphicsTextureView View { get; } = view;

    internal TextureViewInfo ViewInfo { get; } = view.Info;

    internal IReadOnlySet<TextureState> States => _states;

    internal void Access(TextureState next, BarrierScope scope)
    {
        lock (_gate)
        {
            _states.Add(next);
            AddAccess(scope);
        }
    }

    internal override PipelineExternalResource Snapshot()
    {
        var copy = new PipelineTextureResource(Device, Generation, View, _states.FirstOrDefault(), Scope) { Order = Order };
        foreach (TextureState captured in _states)
        {
            copy._states.Add(captured);
        }

        return copy;
    }
}
