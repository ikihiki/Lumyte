// Test-only streaming fake; never used by an engine backend.
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Tests;

/// <summary>Writes current application bytes and retains only resource reference identities.</summary>
internal static class TestShaderDataUpload
{
    /// <summary>Writes a batch without promising failure atomicity or retaining CPU numeric values.</summary>
    /// <typeparam name="T">The application shader data type.</typeparam>
    /// <param name="source">Values read synchronously during this call.</param>
    /// <param name="storage">Idle, mapped upload storage.</param>
    /// <param name="elementOffset">The first destination element.</param>
    /// <param name="metadata">Backend-owned per-element reference metadata.</param>
    /// <param name="encode">The existing backend reference encoder.</param>
    /// <param name="diagnostic">An explicitly enabled compiled layout, or null.</param>
    /// <param name="metadataFactory">The backend-owned immutable dependency factory, or the default identity-only owner.</param>
    /// <remarks>Any failure invalidates metadata for the requested range. Destination bytes may already be partially modified.</remarks>
    public static void CopyFrom<T>(ReadOnlySpan<T> source, IGraphicsBuffer<byte> storage, ulong elementOffset, IShaderReferenceMetadata?[] metadata, Func<ShaderValue, byte[]> encode, ShaderDataLayout? diagnostic, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory = null)
        where T : struct, IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(encode);
        int stride = T.TransferSize;
        if (stride <= 0 || elementOffset > (ulong)metadata.Length || (ulong)source.Length > (ulong)metadata.Length - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        if (source.IsEmpty)
        {
            return;
        }

        ulong offset = checked(elementOffset * (ulong)stride);
        int length = checked(source.Length * stride);
        storage.ValidateByteRange(offset, (ulong)length);
        if (!storage.IsMapped || storage.Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("CopyFrom requires idle mapped upload storage.");
        }

        int start = checked((int)elementOffset);
        Array.Clear(metadata, start, source.Length);
        try
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (diagnostic != null)
                {
                    diagnostic.ValidateTransferLayout(stride, T.TransferFields, T.ShaderTypeName);
                }

                var writer = new TestShaderValueWriter<TestShaderUploadDestination>(new(storage, checked(offset + ((ulong)i * (ulong)stride))), stride, encode, metadataFactory, diagnostic);
                source[i].Write(ref writer);
                IShaderReferenceMetadata value = writer.Complete();
                metadata[start + i] = value;
            }
        }
        catch
        {
            // Clearing this known-valid range cannot replace the original failure.
            Array.Clear(metadata, start, source.Length);
            throw;
        }
    }
}
