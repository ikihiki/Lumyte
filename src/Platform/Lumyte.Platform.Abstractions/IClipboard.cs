namespace Lumyte.Platform.Abstractions;

/// <summary>Accesses Unicode clipboard text on the platform's creating thread.</summary>
public interface IClipboard : IDisposable
{
    /// <summary>Occurs when clipboard content changes, when change notifications are supported.</summary>
    event Action? Changed;

    /// <summary>Gets API capabilities; operating-system permissions can still deny a call.</summary>
    ClipboardFeatures Features { get; }

    /// <summary>Reads Unicode text when available.</summary>
    /// <param name="cancellationToken">Requests cancellation without changing clipboard content.</param>
    /// <returns>The text, or null when the clipboard contains no text.</returns>
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces clipboard content with Unicode text.</summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="cancellationToken">Cancels the operation before native submission.</param>
    /// <returns>A task that completes after the native write.</returns>
    ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default);
}
