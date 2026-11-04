using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Defines ui platform text input target resolver.</summary>
public sealed class UiPlatformTextInputTargetResolver : IUiTextInputTargetResolver
{
    private readonly IUiWindowManager _windows;
    private readonly IWindowInputSource _input;
    private readonly Func<Guid, IWindow?> _resolveNative;

    /// <summary>Initializes a new instance of the <see cref = "UiPlatformTextInputTargetResolver"/> class.</summary>
    /// <param name = "windows">The windows.</param>
    /// <param name = "input">The input.</param>
    /// <param name = "resolveNative">The resolve native.</param>
    public UiPlatformTextInputTargetResolver(IUiWindowManager windows, IWindowInputSource input, Func<Guid, IWindow?> resolveNative)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(resolveNative);
        _windows = windows;
        _input = input;
        _resolveNative = resolveNative;
    }

    /// <summary>Resolve.</summary>
    /// <param name = "focusWindow">The focus window.</param>
    /// <param name = "presentationWindow">The presentation window.</param>
    /// <param name = "surfaceId">The surface id.</param>
    /// <param name = "editorId">The editor id.</param>
    /// <returns>The operation result.</returns>
    public TextInputTarget Resolve(UiWindowId focusWindow, UiWindowId presentationWindow, Guid surfaceId, Guid editorId)
    {
        if (surfaceId == Guid.Empty || editorId == Guid.Empty)
        {
            throw new ArgumentException("Surface and editor identities are required.");
        }

        return new(ResolveWindow(focusWindow), ResolveWindow(presentationWindow), surfaceId, editorId);
    }

    private WindowId ResolveWindow(UiWindowId id)
    {
        if (!_windows.TryGetSnapshot(id, out UiWindowSnapshot? snapshot) || snapshot is null || snapshot.State is not (UiWindowState.Ready or UiWindowState.Open))
        {
            throw new NotSupportedException("The text target is not a live presentation.");
        }

        IWindow? native = _resolveNative(snapshot.SourceId);
        if (native is null || native.State.Lifetime != WindowLifetime.Open || !_input.TryGetInputWindowId(native.Id, out Lumyte.Input.Abstractions.InputWindowId input) || snapshot.InputOrigin != input)
        {
            throw new NotSupportedException("The source and acquisition window cannot be resolved explicitly.");
        }

        return native.Id;
    }
}
