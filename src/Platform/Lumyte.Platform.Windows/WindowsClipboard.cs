using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Lumyte.Platform.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Provides Unicode clipboard text and content-free change notifications.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsClipboard : IClipboard
{
    private const uint UnicodeText = 13;
    private readonly WindowsPlatform _platform;
    private readonly nint _host;
    private bool _listening;
    private bool _disposed;

    internal WindowsClipboard(WindowsPlatform platform)
    {
        _platform = platform;
        _host = platform.CreateHost(message =>
        {
            if (message == 0x31D)
            {
                platform.Notify(() =>
                {
                    if (!_disposed)
                    {
                        platform.NotifyHandlers(Changed);
                    }
                });
            }
        });
        if (!NativeMethods.AddClipboardFormatListener(_host))
        {
            int error = Marshal.GetLastPInvokeError();
            platform.DestroyHost(_host);
            throw new Win32Exception(error);
        }

        _listening = true;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public ClipboardFeatures Features => ClipboardFeatures.TextRead | ClipboardFeatures.TextWrite | ClipboardFeatures.ChangeNotifications;

    /// <inheritdoc/>
    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryGetText(out string? text);
        return ValueTask.FromResult(text);
    }

    /// <inheritdoc/>
    public ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetText(text);
        return ValueTask.CompletedTask;
    }

    /// <summary>Synchronously reads Unicode text when available.</summary>
    /// <param name="text">The clipboard text, or null if absent.</param>
    /// <returns>Whether Unicode text was present.</returns>
    public bool TryGetText(out string? text)
    {
        Check();
        Open();
        try
        {
            text = null;
            if (!NativeMethods.IsClipboardFormatAvailable(UnicodeText))
            {
                return false;
            }

            nint memory = NativeMethods.GetClipboardData(UnicodeText);
            if (memory == 0)
            {
                throw NativeError();
            }

            nuint size = NativeMethods.GlobalSize(memory);
            if (size > int.MaxValue || size < 2 || (size & 1) != 0)
            {
                throw new IOException("Invalid Unicode clipboard allocation.");
            }

            nint pointer = NativeMethods.GlobalLock(memory);
            if (pointer == 0)
            {
                throw NativeError();
            }

            try
            {
                byte[] bytes = new byte[(int)size];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                int length = 0;
                while (length + 1 < bytes.Length && (bytes[length] != 0 || bytes[length + 1] != 0))
                {
                    length += 2;
                }

                if (length == bytes.Length)
                {
                    throw new IOException("Unicode clipboard text has no NUL terminator.");
                }

                text = Encoding.Unicode.GetString(bytes, 0, length);
                return true;
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>Synchronously replaces clipboard content with Unicode text.</summary>
    /// <param name="text">The text to copy.</param>
    public void SetText(string text)
    {
        Check();
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0'))
        {
            throw new ArgumentException("Clipboard text cannot contain NUL.", nameof(text));
        }

        byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
        nint memory = NativeMethods.GlobalAlloc(2, (nuint)bytes.Length);
        if (memory == 0)
        {
            throw NativeError();
        }

        try
        {
            nint pointer = NativeMethods.GlobalLock(memory);
            if (pointer == 0)
            {
                throw NativeError();
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }

            Open();
            try
            {
                if (!NativeMethods.EmptyClipboard() || NativeMethods.SetClipboardData(UnicodeText, memory) == 0)
                {
                    throw NativeError();
                }

                // Successful SetClipboardData transfers ownership to the OS.
                memory = 0;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        finally
        {
            if (memory != 0)
            {
                NativeMethods.GlobalFree(memory);
            }
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
        if (_listening)
        {
            if (!NativeMethods.RemoveClipboardFormatListener(_host))
            {
                throw NativeError();
            }

            _listening = false;
        }

        _platform.DestroyHost(_host);
        _disposed = true;
    }

    private static IOException NativeError() => new("Windows clipboard access failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

    private void Open()
    {
        if (!NativeMethods.OpenClipboard(_host))
        {
            throw NativeError();
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.CheckMutation();
    }
}
