namespace Lumyte.Graphics.Native.Shaders;

/// <summary>An immutable compiler-produced byte layout, not a runtime serializer.</summary>
public sealed class NativeShaderInputLayout
{
    public NativeShaderInputLayout(string abiId, uint size, uint alignment, IEnumerable<NativeShaderInputField> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abiId);
        ArgumentNullException.ThrowIfNull(fields);
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), "A byte layout alignment must be a nonzero power of two.");
        }
        NativeShaderInputField[] snapshot = fields.ToArray();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (NativeShaderInputField field in snapshot)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name) ||
                (ulong)field.Offset + field.Size > size)
            {
                throw new ArgumentException("Fields must have unique names and fit within the layout's byte range.", nameof(fields));
            }
        }
        AbiId = abiId;
        Size = size;
        Alignment = alignment;
        Fields = Array.AsReadOnly(snapshot);
    }

    public string AbiId { get; }
    public uint Size { get; }
    public uint Alignment { get; }
    public IReadOnlyList<NativeShaderInputField> Fields { get; }
}
