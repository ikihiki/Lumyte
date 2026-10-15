using Lumyte.Platform.Abstractions;

namespace Lumyte.Platform.Browser;

/// <summary>Uses the asynchronous, permission-controlled browser Clipboard API.</summary>
public sealed class BrowserClipboard : IClipboard
{
    private readonly BrowserPlatform _platform;
    private int _pending;
    private bool _disposed;

    internal BrowserClipboard(BrowserPlatform platform)
    {
        _platform = platform;
    }

    /// <inheritdoc/>
    public event Action? Changed
    {
        add => Check();
        remove => Check();
    }

    /// <inheritdoc/>
    public ClipboardFeatures Features => ClipboardFeatures.TextRead | ClipboardFeatures.TextWrite;

    /// <inheritdoc/>
    public async ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        Check();
        cancellationToken.ThrowIfCancellationRequested();
        _pending++;
        try
        {
            // The native Promise cannot be cancelled; settle it before releasing the service.
            string text = await BrowserInterop.ReadClipboardAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return text;
        }
        finally
        {
            _pending--;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Check();
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        _pending++;
        try
        {
            // After native submission cancellation must not report an unperformed write.
            await BrowserInterop.WriteClipboardAsync(text);
        }
        finally
        {
            _pending--;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_pending != 0)
        {
            throw new InvalidOperationException("Wait for native clipboard operations before disposing the service.");
        }

        _disposed = true;
        _platform.Release(this);
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.CheckMutation();
    }
}
