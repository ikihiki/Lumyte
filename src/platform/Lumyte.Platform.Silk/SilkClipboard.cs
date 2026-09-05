using Lumyte.Platform;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkClipboard(SilkWindowInput input) : IClipboard
{
    public string? GetText() => input.Keyboards.FirstOrDefault()?.Native.ClipboardText;

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (input.Keyboards.FirstOrDefault() is { } keyboard)
        {
            keyboard.Native.ClipboardText = text;
        }
    }
}
