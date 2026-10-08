namespace Lumyte.Graphics;

/// <summary>Packs caller-owned shader data and element dependencies into an existing CPU Upload range.</summary>
public static class ShaderDataTransfer
{
    /// <summary>Packs values without allocating staging, issuing GPU copies, submitting, or waiting.</summary>
    /// <typeparam name="T">The caller-owned logical element type.</typeparam>
    /// <param name="destination">The exact-size idle CPU Upload range.</param>
    /// <param name="values">The logical elements to serialize.</param>
    /// <param name="layout">The matching reflected wire schema.</param>
    /// <param name="serializer">The caller-owned mapping of fields and opaque references.</param>
    public static void CopyFrom<T>(this BufferSlice<byte> destination, ReadOnlySpan<T> values, ShaderDataLayout<T> layout, IShaderDataSerializer<T> serializer)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Driver.PackShaderData(destination.Range, values, layout.Handle, serializer);
    }
}
