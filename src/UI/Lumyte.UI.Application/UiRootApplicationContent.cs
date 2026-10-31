using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

internal sealed class UiRootApplicationContent(Func<UiApplicationWindowContext, Widget> factory) : IUiApplicationContent, IUiApplicationGraphicsContent
{
    public bool IsStopped { get; private set; }

    public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken) => context.View.SetInitialRootAsync(UiSurface.CaptureBuild(() => factory(context)), cancellationToken);

    public void Advance()
    {
    }

    public void BeginStop() => IsStopped = true;

    public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context)
    {
        context.View.CurrentSurface!.Rebuild(UiSurface.CaptureBuild(() => factory(context)));
        return ValueTask.CompletedTask;
    }

    public ValueTask RetireAsync() => ValueTask.CompletedTask;

    public ValueTask ReleaseAsync() => ValueTask.CompletedTask;
}
