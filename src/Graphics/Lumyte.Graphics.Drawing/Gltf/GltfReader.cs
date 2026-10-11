using System.Text.Json;

namespace Lumyte.Graphics.Drawing.Gltf;

internal sealed partial class GltfReader(JsonElement root)
{
    private readonly JsonElement _root = root;
    private readonly JsonElement[] _views = Array(root, "bufferViews");
    private readonly JsonElement[] _accessors = Array(root, "accessors");
    private ReadOnlyMemory<byte>[] _buffers = [];
    private GltfTexture[] _textures = [];
    private GltfMaterial[] _materials = [];

    internal async Task<GltfModel> ReadAsync(
        ReadOnlyMemory<byte> binary,
        Func<string, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? resolver,
        CancellationToken token)
    {
        string? version = _root.GetProperty("asset").GetProperty("version").GetString();
        if (version != "2.0")
        {
            throw new NotSupportedException("Only glTF 2.0 is supported.");
        }

        if (_root.GetProperty("asset").TryGetProperty("minVersion", out JsonElement minimum) && minimum.GetString() != "2.0")
        {
            throw new NotSupportedException("The asset requires a newer glTF version.");
        }

        foreach (JsonElement extension in Array(_root, "extensionsRequired"))
        {
            if (extension.GetString() != "KHR_materials_unlit")
            {
                throw new NotSupportedException("Required glTF extension is unsupported: " + extension.GetString());
            }
        }

        if (Array(_root, "skins").Length != 0 || Array(_root, "animations").Length != 0)
        {
            throw new NotSupportedException("Skins and animations require a deformation/animation renderer.");
        }

        JsonElement[] buffers = Array(_root, "buffers");
        _buffers = new ReadOnlyMemory<byte>[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int length = buffers[i].GetProperty("byteLength").GetInt32();
            if (length < 0)
            {
                throw new InvalidDataException("Buffer length must be nonnegative.");
            }

            ReadOnlyMemory<byte> content;
            if (buffers[i].TryGetProperty("uri", out JsonElement uri))
            {
                content = await ReadUriAsync(uri.GetString() ?? throw new InvalidDataException("A resource URI cannot be null."), resolver, token).ConfigureAwait(false);
            }
            else if (i == 0 && !binary.IsEmpty)
            {
                content = binary;
                if (content.Length - length > 3)
                {
                    throw new InvalidDataException("GLB BIN padding exceeds three bytes.");
                }
            }
            else
            {
                throw new InvalidDataException("A buffer has no URI or GLB BIN data.");
            }

            if (content.Length < length)
            {
                throw new InvalidDataException("Buffer is shorter than its declared byteLength.");
            }

            _buffers[i] = content[..length].ToArray();
        }

        JsonElement[] imageEntries = Array(_root, "images");
        var images = new GltfImage[imageEntries.Length];
        for (int i = 0; i < images.Length; i++)
        {
            JsonElement image = imageEntries[i];
            ReadOnlyMemory<byte> content = image.TryGetProperty("uri", out JsonElement uri)
                ? await ReadUriAsync(uri.GetString() ?? throw new InvalidDataException("A resource URI cannot be null."), resolver, token).ConfigureAwait(false)
                : View(image.GetProperty("bufferView").GetInt32());
            string? mime = String(image, "mimeType");
            if (mime != null && mime != "image/png" && mime != "image/jpeg")
            {
                throw new NotSupportedException("Only PNG and JPEG model images are supported.");
            }

            images[i] = new(String(image, "name"), mime, content.ToArray());
        }

        _textures = ReadTextures(images.Length);
        _materials = ReadMaterials();
        GltfMesh[] meshes = ReadMeshes(token);
        GltfInstance[] instances = ReadInstances(meshes.Length, token);
        return new(meshes, _materials, _textures, images, instances);
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadUriAsync(
        string uri,
        Func<string, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? resolver,
        CancellationToken token)
    {
        if (uri.StartsWith("data:", StringComparison.Ordinal))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.Ordinal))
            {
                throw new NotSupportedException("Only base64 data URIs are supported.");
            }

            return Convert.FromBase64String(uri[(comma + 1)..]);
        }

        if (resolver == null)
        {
            throw new InvalidDataException("An external resource resolver is required for " + uri);
        }

        return await resolver(uri, token).ConfigureAwait(false);
    }

    private static JsonElement[] Array(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value)
        ? value.EnumerateArray().ToArray() : [];

    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;

    private static int Int(JsonElement element, string property, int fallback = 0) => element.TryGetProperty(property, out JsonElement value) ? value.GetInt32() : fallback;

    private static float Float(JsonElement element, string property, float fallback)
    {
        float value = element.TryGetProperty(property, out JsonElement number) ? number.GetSingle() : fallback;
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("Nonfinite value: " + property);
        }

        return value;
    }

    private static bool Bool(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.GetBoolean();

    private static float[] Floats(JsonElement element, string property, float[] fallback)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return fallback;
        }

        float[] values = value.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (values.Length != fallback.Length || values.Any(x => !float.IsFinite(x)))
        {
            throw new InvalidDataException("Invalid vector or matrix: " + property);
        }

        return values;
    }

    private static void CheckIndex(int index, int count, string label)
    {
        if ((uint)index >= (uint)count)
        {
            throw new InvalidDataException("Invalid " + label + " index.");
        }
    }

    private ReadOnlyMemory<byte> View(int index)
    {
        JsonElement[] views = _views;
        CheckIndex(index, views.Length, "bufferView");
        JsonElement view = views[index];
        int buffer = view.GetProperty("buffer").GetInt32();
        CheckIndex(buffer, _buffers.Length, "buffer");
        int offset = Int(view, "byteOffset");
        int length = view.GetProperty("byteLength").GetInt32();
        if (offset < 0 || length < 0 || offset > _buffers[buffer].Length - length)
        {
            throw new InvalidDataException("Buffer view exceeds its buffer.");
        }

        return _buffers[buffer].Slice(offset, length);
    }
}
