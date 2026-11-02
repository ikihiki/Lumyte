using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation;

internal sealed class UiRegisteredNavigationContent<TArgs>(UiPageRegistry registry, UiPageKey<TArgs> initial, TArgs arguments, UiNavigationOptions? options, Action<UiNavigationResult>? completed) : IUiApplicationContent, IUiApplicationGraphicsContent
{
    private UiNavigationApplicationContent? _inner;
    private bool _stopping;

    public bool IsStopped => _stopping && (_inner?.IsStopped ?? true);

    public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
    {
        _inner = new(registry.CreateProvider(context), navigation => navigation.RequestNavigate(initial, arguments), options, completed);
        return _inner.InitializeAsync(context, cancellationToken);
    }

    public void Advance() => _inner?.Advance();

    public void BeginStop()
    {
        _stopping = true;
        _inner?.BeginStop();
    }

    public ValueTask RetireAsync() => _inner?.RetireAsync() ?? ValueTask.CompletedTask;

    public ValueTask ReleaseAsync() => _inner?.ReleaseAsync() ?? ValueTask.CompletedTask;

    public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => ValueTask.CompletedTask;
}
