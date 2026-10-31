using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

/// <summary>Describes a normal application window without generating resources.</summary>
public sealed class UiWindowDeclaration
{
    internal UiWindowDeclaration(string key, string title, Func<UiApplicationWindowContext, Widget>? root, Func<IServiceProvider, IUiApplicationContent>? content, bool isOpen, string? owner, Vector2 initialClientSize, WindowPresentation presentation, Action<UiWindowCloseRequest>? onCloseRequested, Action<UiApplicationWindowResult>? onOpened, Action<UiApplicationWindowResult>? onClosed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(title);
        if ((root is null) == (content is null))
        {
            throw new ArgumentException("Specify exactly one root or content factory.");
        }

        UiApplicationBuilder.ValidateSize(initialClientSize);
        Key = key;
        Title = title;
        Root = root;
        Content = content;
        IsOpen = isOpen;
        Owner = owner;
        InitialClientSize = initialClientSize;
        Presentation = presentation;
        OnCloseRequested = onCloseRequested;
        OnOpened = onOpened;
        OnClosed = onClosed;
    }

    /// <summary>Gets the stable declaration key.</summary>
    public string Key { get; }

    /// <summary>Gets the current title.</summary>
    public string Title { get; }

    /// <summary>Gets a value indicating whether the model requests an open instance.</summary>
    public bool IsOpen { get; }

    /// <summary>Gets the optional logical owner key.</summary>
    public string? Owner { get; }

    /// <summary>Gets the initial logical client size.</summary>
    public Vector2 InitialClientSize { get; }

    /// <summary>Gets the requested presentation.</summary>
    public WindowPresentation Presentation { get; }

    internal Func<UiApplicationWindowContext, Widget>? Root { get; }

    internal Func<IServiceProvider, IUiApplicationContent>? Content { get; }

    internal Action<UiWindowCloseRequest>? OnCloseRequested { get; }

    internal Action<UiApplicationWindowResult>? OnOpened { get; }

    internal Action<UiApplicationWindowResult>? OnClosed { get; }
}
