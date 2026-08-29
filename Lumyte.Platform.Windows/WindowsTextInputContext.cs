using Lumyte.Input;

namespace Lumyte.Platform.Windows;

public sealed class WindowsTextInputContext : ITextInputContext, IDisposable
{
    private readonly WindowsWindow window;
    private TsfThread? thread;
    private TsfDocument? document;
    private ITextInputClient? client;
    private char? highSurrogate;
    private bool keyEatenByTip;

    internal WindowsTextInputContext(WindowsWindow window)
    {
        this.window = window;
        thread = TsfThread.Acquire();
        document = thread?.CreateDocument(
            () => client,
            window.GetOpenHandle,
            () => window.ScaleFactor);
        if (thread is not null && document is null)
        {
            thread.Release();
            thread = null;
        }

        window.FocusChanged += OnFocusChanged;
    }

    public bool IsAvailable => document is not null;

    public bool IsActive => client is not null;

    public void Activate(ITextInputClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client?.SetComposition(default, null);
        this.client?.SetCandidates(null);
        this.client = client;
        if (window.IsFocused)
        {
            document?.Focus();
        }
    }

    public void Deactivate()
    {
        client?.SetComposition(default, null);
        client?.SetCandidates(null);
        client = null;
        highSurrogate = null;
        if (document is not null)
        {
            thread?.ClearFocus(document);
        }
    }

    public void NotifyTextChanged(TextChange change) => document?.Store.NotifyTextChanged(change);

    public void NotifySelectionChanged() => document?.Store.NotifySelectionChanged();

    public void NotifyLayoutChanged() => document?.Store.NotifyLayoutChanged();

    public void Dispose()
    {
        window.FocusChanged -= OnFocusChanged;
        Deactivate();
        document?.Dispose();
        document = null;
        thread?.Release();
        thread = null;
    }

    internal bool HandleKeyDown(ushort virtualKey, nint keyData)
    {
        keyEatenByTip = IsActive && (thread?.HandleKeyDown(virtualKey, keyData) ?? false);
        return keyEatenByTip;
    }

    internal bool HandleKeyUp(ushort virtualKey, nint keyData) =>
        IsActive && (thread?.HandleKeyUp(virtualKey, keyData) ?? false);

    internal void DispatchCharacter(char value)
    {
        if (!IsActive || keyEatenByTip || client is null)
        {
            return;
        }

        if (char.IsHighSurrogate(value))
        {
            highSurrogate = value;
            return;
        }

        string text;
        if (char.IsLowSurrogate(value) && highSurrogate is char leading)
        {
            text = string.Concat(leading, value);
            highSurrogate = null;
        }
        else
        {
            highSurrogate = null;
            if (value is '\b' or '\u001b')
            {
                return;
            }

            text = value.ToString();
        }

        TextRange selection = client.Selection;
        client.Replace(selection, text);
        keyEatenByTip = false;
    }

    private void OnFocusChanged(object? sender, WindowFocusChangedEventArgs eventArgs)
    {
        if (eventArgs.IsFocused && IsActive)
        {
            document?.Focus();
        }
        else if (!eventArgs.IsFocused && document is not null)
        {
            thread?.ClearFocus(document);
        }
    }
}
