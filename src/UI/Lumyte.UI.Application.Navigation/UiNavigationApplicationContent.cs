using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Connects one navigator to the standard application's staged content lifetime.</summary>
/// <param name="provider">The borrowed page activation provider.</param>
/// <param name="initialRequest">Queues the initial destination once, on the owner.</param>
/// <param name="options">The history bounds.</param>
/// <param name="onCompleted">Observes settled requests on the owner.</param>
public sealed class UiNavigationApplicationContent(IUiPageActivationProvider provider, Func<IUiNavigator, UiNavigationRequest> initialRequest, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null) : IUiApplicationContent, IUiApplicationGraphicsContent
{
    private UiNavigator? _navigator;
    private bool _stopping;
    private UiApplicationWindowContext? _context;

    /// <inheritdoc/>
    public bool IsStopped => _stopping && (_navigator?.IsStopped ?? true);

    /// <summary>Gets the borrowed navigator after initialization starts.</summary>
    public IUiNavigator Navigation => _navigator ?? throw new InvalidOperationException("Navigation has not initialized.");

    /// <summary>Gets the borrowed window context during the content lifetime.</summary>
    public UiApplicationWindowContext WindowContext => _context ?? throw new InvalidOperationException("The window context is unavailable.");

    /// <inheritdoc/>
    public async ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(initialRequest);
        _context = context;
        _navigator = new(provider, context.View.CreateNavigationHost(), options, onCompleted);
        UiNavigationRequest request = initialRequest(_navigator);
        using CancellationTokenRegistration registration = cancellationToken.Register(request.Cancel);
        UiNavigationResult result = await request.Completion;
        if (result.Status == UiNavigationStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.Status != UiNavigationStatus.Committed)
        {
            throw new UiApplicationException(result.Code, result.Error);
        }
    }

    /// <inheritdoc/>
    public void Advance() => _navigator?.Advance();

    /// <inheritdoc/>
    public void BeginStop()
    {
        _stopping = true;
        _navigator?.BeginStop();
    }

    /// <inheritdoc/>
    public ValueTask RetireAsync() => _navigator?.RetireAsync() ?? ValueTask.CompletedTask;

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync()
    {
        try
        {
            if (_navigator is not null)
            {
                await _navigator.ReleaseAsync();
            }
        }
        finally
        {
            _context = null;
        }
    }

    /// <inheritdoc/>
    public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => ValueTask.CompletedTask;
}
