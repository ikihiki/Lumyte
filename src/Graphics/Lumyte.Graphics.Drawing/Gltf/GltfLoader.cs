using System.Buffers.Binary;
using System.Text.Json;

namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Loads static glTF 2.0 and GLB scenes without GPU access or implicit network requests.</summary>
public static class GltfLoader
{
    /// <summary>Loads a glTF or GLB file and relative local buffer/image files.</summary>
    /// <param name="path">The model file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fully loaded CPU model.</returns>
    public static async Task<GltfModel> LoadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        byte[] file = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(file, ResolveAsync, cancellationToken).ConfigureAwait(false);

        async ValueTask<ReadOnlyMemory<byte>> ResolveAsync(string uri, CancellationToken token)
        {
            string relative = Uri.UnescapeDataString(uri);
            if (relative.Contains('\\') || relative.Contains('?') || relative.Contains('#') || Uri.TryCreate(relative, UriKind.Absolute, out _))
            {
                throw new NotSupportedException("Only relative local resource URIs are supported by LoadFileAsync.");
            }

            string resource = Path.GetFullPath(Path.Combine(directory, relative));
            string relativePath = Path.GetRelativePath(directory, resource);
            if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A resource URI escapes the model directory.");
            }

            return await File.ReadAllBytesAsync(resource, token).ConfigureAwait(false);
        }
    }

    /// <summary>Loads an in-memory glTF or GLB, resolving external resources only through the supplied callback.</summary>
    /// <param name="file">The JSON or GLB bytes.</param>
    /// <param name="resolveResource">The optional external buffer/image resolver.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owned CPU scene; unsupported deformation or required extensions are rejected.</returns>
    public static async Task<GltfModel> LoadAsync(
        ReadOnlyMemory<byte> file,
        Func<string, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? resolveResource = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            (ReadOnlyMemory<byte> json, ReadOnlyMemory<byte> binary) = SplitContainer(file);
            using var document = JsonDocument.Parse(json);
            var reader = new GltfReader(document.RootElement);
            return await reader.ReadAsync(binary, resolveResource, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("Invalid glTF data: " + exception.Message, exception);
        }
    }

    private static (ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte> Binary) SplitContainer(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;
        if (bytes.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546c67)
        {
            return (file, ReadOnlyMemory<byte>.Empty);
        }

        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 2 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != bytes.Length)
        {
            throw new InvalidDataException("Invalid GLB version or length.");
        }

        ReadOnlyMemory<byte> json = default;
        ReadOnlyMemory<byte> binary = default;
        bool hasBinary = false;
        int offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new InvalidDataException("Truncated GLB chunk header.");
            }

            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;
            if (length % 4 != 0 || length > bytes.Length - offset)
            {
                throw new InvalidDataException("Invalid GLB chunk length.");
            }

            if (offset == 20 && type != 0x4e4f534a)
            {
                throw new InvalidDataException("GLB must begin with a JSON chunk.");
            }

            if (type == 0x4e4f534a)
            {
                if (!json.IsEmpty)
                {
                    throw new InvalidDataException("Duplicate GLB JSON chunk.");
                }

                json = file.Slice(offset, length);
            }
            else if (type == 0x004e4942)
            {
                if (hasBinary)
                {
                    throw new InvalidDataException("Duplicate GLB BIN chunk.");
                }

                hasBinary = true;
                binary = file.Slice(offset, length);
            }

            offset += length;
        }

        return (json, binary);
    }
}
