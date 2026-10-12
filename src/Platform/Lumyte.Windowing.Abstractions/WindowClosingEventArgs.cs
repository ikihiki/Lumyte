namespace Lumyte.Windowing.Abstractions;

/// <summary>Initializes a new instance of the <see cref="WindowClosingEventArgs"/> class.</summary>
/// <param name="window">The window value.</param>
public sealed class WindowClosingEventArgs(WindowId window) : EventArgs
{
    /// <summary>Gets Window.</summary>
    public WindowId Window { get; } = window;

    /// <summary>Gets or sets a value indicating whether Cancel is enabled.</summary>
    public bool Cancel { get; set; }
}
