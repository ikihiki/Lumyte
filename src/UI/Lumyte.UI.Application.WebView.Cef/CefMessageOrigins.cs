using System.Text.Json;

namespace Lumyte.UI.Application.WebView.Cef;

internal static class CefMessageOrigins
{
    internal static string Canonical(string origin)
    {
        if (origin.Contains('*') || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("cef-message-origin-invalid", nameof(origin));
        }

        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant();
    }

    internal static string[] Snapshot(IReadOnlyList<string> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        if (origins.Count > 16)
        {
            throw new ArgumentException("cef-message-origin-limit", nameof(origins));
        }

        return origins.Select(Canonical).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static bool Allows(string[] origins, Uri source) => source.IsAbsoluteUri &&
        source.Scheme is "http" or "https" && source.UserInfo.Length == 0 &&
        origins.Contains(source.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant(), StringComparer.Ordinal);

    internal static byte[] Start(Uri url, string[] origins)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("url", url.AbsoluteUri);
            writer.WriteStartArray("origins");
            foreach (string origin in origins)
            {
                writer.WriteStringValue(origin);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (stream.Length > 4096)
        {
            throw new ArgumentException("cef-start-too-large");
        }

        return stream.ToArray();
    }
}
