using System.Security.Cryptography;
using System.Text.Json;

namespace Lumyte.Build;

// Cache installed artifacts only, never the source/CMake tree.
internal static class DxcCache
{
    internal static readonly string[] Artifacts = ["lib/libdxcompiler.so", "lib/libdxil.so", "LICENSE.TXT"];

    internal static string HashFile(string path)
    {
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    internal static string Fingerprint(IEnumerable<KeyValuePair<string, string>> inputs) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray())));

    internal static bool Verify(string root, string fingerprint)
    {
        try
        {
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(root, "provenance.json")));
            return manifest is not null && manifest.Schema == 1 && manifest.Fingerprint == fingerprint &&
                manifest.Inputs is not null && Fingerprint(manifest.Inputs) == fingerprint &&
                manifest.Hashes is not null && manifest.Hashes.Count == Artifacts.Length &&
                Artifacts.All(name => manifest.Hashes.TryGetValue(name, out string? hash) &&
                    new FileInfo(Path.Combine(root, name)).Length > 0 && HashFile(Path.Combine(root, name)) == hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    internal static void Publish(string root, IReadOnlyDictionary<string, string> inputs)
    {
        Dictionary<string, string> hashes = Artifacts.ToDictionary(name => name, name =>
        {
            string path = Path.Combine(root, name);
            if (new FileInfo(path).Length == 0)
            {
                throw new InvalidDataException("Empty DXC artifact: " + name);
            }

            return HashFile(path);
        }, StringComparer.Ordinal);
        string temporary = Path.Combine(root, "provenance.json.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Manifest(1, Fingerprint(inputs), inputs, hashes)) + "\n");
        File.Move(temporary, Path.Combine(root, "provenance.json"), overwrite: true);
    }

    private sealed record Manifest(int Schema, string Fingerprint, IReadOnlyDictionary<string, string> Inputs, Dictionary<string, string> Hashes);
}
