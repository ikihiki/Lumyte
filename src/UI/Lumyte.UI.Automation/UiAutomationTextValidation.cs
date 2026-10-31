using System.Text;

namespace Lumyte.UI.Automation;

internal static class UiAutomationTextValidation
{
    internal static void Validate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 32768)
        {
            throw new ArgumentException("The text input limit was exceeded.", nameof(text));
        }

        _ = new UTF8Encoding(false, true).GetByteCount(text);
    }
}
