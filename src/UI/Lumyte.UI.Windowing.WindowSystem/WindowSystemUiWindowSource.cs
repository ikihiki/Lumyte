using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.WindowSystem;

/// <summary>Adapts a borrowed existing native WindowNode with an explicit acquisition source.</summary>
public sealed class WindowSystemUiWindowSource : IUiWindowSource, IUiWindowPresentationStager
{
    private readonly IWindow _window;

    private readonly IWindowInputSource _input;

    private readonly IUiNativePresentationFactory _presentations;

    private readonly Guid _scope;

    private UiWindowSourceSnapshot? _previous;

    private ulong _revision;

    /// <summary>Initializes a new instance of the <see cref="WindowSystemUiWindowSource"/> class.</summary>
    /// <param name="node">A borrowed native presentation root.</param>
    /// <param name="input">The acquisition source for that window.</param>
    /// <param name="presentations">The borrowed renderer factory.</param>
    public WindowSystemUiWindowSource(WindowNode node, IWindowInputSource input, IUiNativePresentationFactory presentations)
        : this(node, input, presentations, Guid.NewGuid())
    {
    }

    internal WindowSystemUiWindowSource(WindowNode node, IWindowInputSource input, IUiNativePresentationFactory presentations, Guid scope)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(presentations);
        Node = node;
        _window = node.NativeWindow;
        _input = input;
        _presentations = presentations;
        _scope = scope;
    }

    /// <inheritdoc/>
    public Guid SourceId { get; } = Guid.NewGuid();

    internal WindowNode Node { get; }

    internal IWindow Window => _window;

    /// <inheritdoc/>
    public UiWindowSourceSnapshot CaptureState()
    {
        WindowState state = _window.State;
        InputWindowId? origin = _input.TryGetInputWindowId(_window.Id, out InputWindowId id) ? id : null;
        var next = new UiWindowSourceSnapshot(0, origin, state.ClientSize, state.Scale, state.IsVisible, state.IsActive, state.Lifetime == WindowLifetime.Closed, _scope);
        if (_previous is not null && _previous with
        {
            Revision = 0,
        }

        == next)
        {
            return _previous;
        }

        _previous = next with
        {
            Revision = checked(++_revision),
        };
        return _previous;
    }

    /// <inheritdoc/>
    public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken) => _presentations.PrepareAsync(_window, view, surface, cancellationToken);

    /// <inheritdoc/>
    public IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate) => _presentations is IUiNativePresentationStager stager ? stager.BeginReplacement(_window, current, view, candidate) : throw new NotSupportedException("presentation-replacement-unavailable");

    /// <inheritdoc/>
    public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
    {
        transformed = position;
        return ReferenceEquals(this, destination);
    }
}
