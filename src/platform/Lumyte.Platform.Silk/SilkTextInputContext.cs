using Lumyte.Input;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkTextInputContext : ITextInputContext, IDisposable
{
    private readonly IReadOnlyList<SilkKeyboard> keyboards;
    private ITextInputClient? client;

    internal SilkTextInputContext(IReadOnlyList<SilkKeyboard> keyboards)
    {
        this.keyboards = keyboards;
    }

    public bool IsAvailable => keyboards.Count > 0;

    public bool IsActive => client is not null;

    public void Activate(ITextInputClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Deactivate();
        this.client = client;
        foreach (SilkKeyboard keyboard in keyboards)
        {
            keyboard.Native.KeyChar += OnKeyChar;
            keyboard.Native.BeginInput();
        }
    }

    public void Deactivate()
    {
        if (client is null)
        {
            return;
        }

        foreach (SilkKeyboard keyboard in keyboards)
        {
            keyboard.Native.KeyChar -= OnKeyChar;
            keyboard.Native.EndInput();
        }

        client.SetComposition(default, null);
        client.SetCandidates(null);
        client = null;
    }

    public void NotifyTextChanged(TextChange change)
    {
    }

    public void NotifySelectionChanged()
    {
    }

    public void NotifyLayoutChanged()
    {
    }

    public void Dispose() => Deactivate();

    private void OnKeyChar(Silk.NET.Input.IKeyboard _, char character)
    {
        if (client is null)
        {
            return;
        }

        TextRange selection = client.Selection;
        client.Replace(selection, character.ToString());
        int caret = selection.Start + 1;
        client.Select(new(caret, 0));
    }
}
