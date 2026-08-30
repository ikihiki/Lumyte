using System.Text;

namespace Lumyte.Resources;

/// <summary>Builds a canonical selector from escaped path segments.</summary>
public sealed class ResourceSelectorBuilder
{
    private readonly StringBuilder text = new();

    internal bool IsEmpty => text.Length == 0;

    public void WriteSegment(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (text.Length > 0)
        {
            text.Append('/');
        }

        text.Append(Uri.EscapeDataString(value));
    }

    internal string Build() => text.ToString();
}
