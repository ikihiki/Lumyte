using System.Globalization;
using System.Text.Json;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class GltfDocument(ResourceId id, JsonDocument json)
{
    public ResourceId Id => id;

    internal JsonElement Root => json.RootElement;

    internal static int Select(ResourceAddress address, string kind)
    {
        string prefix = kind + "/";
        if (!address.Subresource.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(address.Subresource.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw new ArgumentException($"Expected {kind}/<index>.", nameof(address));
        }

        return index;
    }

    internal void Dispose() => json.Dispose();

    internal ResourceId Resolve(string uri)
    {
        if (uri.Length == 0 || uri.StartsWith('/') || uri.Contains('\\') || uri.Contains(':') || uri.Contains('?') || uri.Contains('#'))
        {
            throw new NotSupportedException("Only relative external file URIs are supported.");
        }

        string decoded = Uri.UnescapeDataString(uri);
        string parent = Id.ToString().Contains('/') ? Id.ToString()[..Id.ToString().LastIndexOf('/')] + "/" : string.Empty;
        var parts = new List<string>();
        foreach (string part in (parent + decoded).Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException("The URI escapes the asset root.");
                }

                parts.RemoveAt(parts.Count - 1);
            }
            else if (part != "." && part.Length != 0)
            {
                parts.Add(part);
            }
        }

        return new(string.Join('/', parts));
    }
}
