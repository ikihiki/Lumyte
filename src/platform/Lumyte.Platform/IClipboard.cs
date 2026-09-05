namespace Lumyte.Platform;

public interface IClipboard
{
    string? GetText();

    void SetText(string text);
}
