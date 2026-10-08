using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Wgpu;

internal sealed class ShaderDataWriter : IShaderDataWriter
{
    private readonly WgpuDevice _owner;
    private readonly ShaderDataSchema _schema;
    private readonly byte[] _bytes;
    private readonly List<DescriptorRegistration> _dependencies;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);
    private int _rowOffset;
    private bool _active;

    internal ShaderDataWriter(WgpuDevice owner, ShaderDataSchema schema, byte[] bytes, List<DescriptorRegistration> dependencies)
    {
        (_owner, _schema, _bytes, _dependencies) = (owner, schema, bytes, dependencies);
    }

    public void Write<TValue>(string fieldName, TValue value)
        where TValue : unmanaged
    {
        ShaderDataField field = GetField(fieldName);
        Type valueType = typeof(TValue);
        string scalarType;
        int count;
        if (valueType == typeof(float) || valueType == typeof(Vector2) || valueType == typeof(Vector3) || valueType == typeof(Vector4))
        {
            scalarType = "float32";
            count = valueType == typeof(float) ? 1 : valueType == typeof(Vector2) ? 2 : valueType == typeof(Vector3) ? 3 : 4;
        }
        else if (valueType == typeof(uint) || valueType == typeof(int))
        {
            scalarType = valueType == typeof(uint) ? "uint32" : "int32";
            count = 1;
        }
        else
        {
            throw new NotSupportedException("Unsupported numeric shader field type.");
        }

        if (field.ScalarType != scalarType || field.ElementCount != count)
        {
            throw new ArgumentException("Numeric value does not match the reflected field type.", nameof(value));
        }

        ReadOnlySpan<TValue> values = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
        if (scalarType == "float32")
        {
            ReadOnlySpan<float> components = MemoryMarshal.Cast<TValue, float>(values);
            foreach (float component in components)
            {
                if (!float.IsFinite(component))
                {
                    throw new ArgumentException("Shader field components must be finite.", nameof(value));
                }
            }
        }

        MarkWritten(fieldName);
        ReadOnlySpan<uint> bits = MemoryMarshal.Cast<TValue, uint>(values);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_rowOffset + field.Offset + (i * 4), 4), bits[i]);
        }
    }

    public void WriteTextureReference(string fieldName, TextureDescriptorReference? reference) => WriteReference(fieldName, reference?.Handle, typeof(TextureView), reference.HasValue);

    public void WriteSamplerReference(string fieldName, SamplerDescriptorReference? reference) => WriteReference(fieldName, reference?.Handle, typeof(Sampler), reference.HasValue);

    public void WriteBufferReference<T>(string fieldName, BufferDescriptorReference<T> reference)
        where T : unmanaged => WriteReference(fieldName, reference.Handle, typeof(WgpuBuffer), true);

    internal void BeginRow(int rowOffset)
    {
        _rowOffset = rowOffset;
        _written.Clear();
        _active = true;
    }

    internal void EndRow() => _active = false;

    private void WriteReference(string fieldName, object? handle, Type resourceType, bool required)
    {
        ShaderDataField field = GetField(fieldName);
        if (field.ScalarType != "uint32" || field.ElementCount != 1)
        {
            throw new ArgumentException("Resource references require a reflected UInt32 field.", nameof(fieldName));
        }

        uint identity = 0;
        if (required)
        {
            if (handle is not DescriptorRegistration registration || !resourceType.IsInstanceOfType(registration.Resource))
            {
                throw new ArgumentException("Invalid logical descriptor reference.");
            }

            registration.Check(_owner);
            registration.Resource.Check(_owner);
            identity = registration.Identity;
            _dependencies.Add(registration);
        }

        MarkWritten(fieldName);
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_rowOffset + field.Offset, 4), identity);
    }

    private ShaderDataField GetField(string fieldName)
    {
        if (!_active || Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("Shader data writer is valid only in its synchronous serialization callback.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (!_schema.Fields.TryGetValue(fieldName, out ShaderDataField? field))
        {
            throw new ArgumentException("Field does not exist in the reflected shader element.", nameof(fieldName));
        }

        return field;
    }

    private void MarkWritten(string fieldName)
    {
        if (!_written.Add(fieldName))
        {
            throw new ArgumentException("Each shader field may be written only once per element.", nameof(fieldName));
        }
    }
}
