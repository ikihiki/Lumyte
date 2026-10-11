using System.Buffers.Binary;
using System.Text.Json;

namespace Lumyte.Graphics.Drawing.Gltf;

internal sealed partial class GltfReader
{
    private static int ComponentSize(int type) => type switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException("Unsupported accessor component type."),
    };

    private static double Component(ReadOnlySpan<byte> bytes, int type, bool normalized) => type switch
    {
        5120 => normalized ? Math.Max((sbyte)bytes[0] / 127.0, -1) : (sbyte)bytes[0],
        5121 => normalized ? bytes[0] / 255.0 : bytes[0],
        5122 => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32767.0, -1) : BinaryPrimitives.ReadInt16LittleEndian(bytes),
        5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) / 65535.0 : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        5125 when !normalized => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        5126 when !normalized => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        _ => throw new InvalidDataException("Invalid normalized accessor component type."),
    };

    private static void CheckAccessorRange(int offset, int count, int stride, int elementSize, int length, int alignment)
    {
        if (offset < 0 || offset % alignment != 0 || (long)offset + ((long)(count - 1) * stride) + elementSize > length)
        {
            throw new InvalidDataException("Accessor data exceeds or misaligns its buffer view.");
        }
    }

    private static void ReadElement(ReadOnlySpan<byte> source, double[] destination, int offset, int components, int type, bool normalized)
    {
        int size = ComponentSize(type);
        for (int i = 0; i < components; i++)
        {
            double value = Component(source[(i * size)..], type, normalized);
            if (!double.IsFinite(value))
            {
                throw new InvalidDataException("Accessor contains a nonfinite value.");
            }

            destination[offset + i] = value;
        }
    }

    private AccessorValues ReadAccessor(int index)
    {
        JsonElement[] accessors = _accessors;
        CheckIndex(index, accessors.Length, "accessor");
        JsonElement accessor = accessors[index];
        int count = accessor.GetProperty("count").GetInt32();
        int components = String(accessor, "type") switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new NotSupportedException("Only scalar and vector mesh accessors are supported."),
        };
        int type = accessor.GetProperty("componentType").GetInt32();
        int componentSize = ComponentSize(type);
        int elementSize = components * componentSize;
        bool normalized = Bool(accessor, "normalized");
        if (count <= 0 || (normalized && type is 5125 or 5126))
        {
            throw new InvalidDataException("Invalid accessor count or normalization.");
        }

        double[] values = new double[checked(count * components)];
        int offset = Int(accessor, "byteOffset");
        if (accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
        {
            int viewId = viewIndex.GetInt32();
            ReadOnlySpan<byte> bytes = View(viewId).Span;
            JsonElement view = _views[viewId];
            int stride = Int(view, "byteStride", elementSize);
            if (((long)Int(view, "byteOffset") + offset) % componentSize != 0 || stride < elementSize || stride % componentSize != 0 || (view.TryGetProperty("byteStride", out _) && (stride < 4 || stride > 252 || stride % 4 != 0)))
            {
                throw new InvalidDataException("Invalid accessor stride.");
            }

            CheckAccessorRange(offset, count, stride, elementSize, bytes.Length, componentSize);
            for (int i = 0; i < count; i++)
            {
                ReadElement(bytes.Slice(checked(offset + (i * stride)), elementSize), values, i * components, components, type, normalized);
            }
        }
        else if (offset != 0)
        {
            throw new InvalidDataException("An accessor without a bufferView must have zero byteOffset.");
        }

        if (accessor.TryGetProperty("sparse", out JsonElement sparse))
        {
            int sparseCount = sparse.GetProperty("count").GetInt32();
            if (sparseCount <= 0 || sparseCount > count)
            {
                throw new InvalidDataException("Invalid sparse accessor count.");
            }

            JsonElement indices = sparse.GetProperty("indices");
            int indexType = indices.GetProperty("componentType").GetInt32();
            if (indexType is not (5121 or 5123 or 5125))
            {
                throw new InvalidDataException("Sparse indices must be unsigned integers.");
            }

            int indexSize = ComponentSize(indexType);
            int indexView = indices.GetProperty("bufferView").GetInt32();
            int valueView = sparse.GetProperty("values").GetProperty("bufferView").GetInt32();
            ReadOnlySpan<byte> indexBytes = View(indexView).Span;
            ReadOnlySpan<byte> valueBytes = View(valueView).Span;
            if (_views[indexView].TryGetProperty("byteStride", out _) || _views[valueView].TryGetProperty("byteStride", out _))
            {
                throw new InvalidDataException("Sparse accessor views must be tightly packed.");
            }

            int indexOffset = Int(indices, "byteOffset");
            int valueOffset = Int(sparse.GetProperty("values"), "byteOffset");
            if (((long)Int(_views[indexView], "byteOffset") + indexOffset) % indexSize != 0 || ((long)Int(_views[valueView], "byteOffset") + valueOffset) % componentSize != 0)
            {
                throw new InvalidDataException("Sparse accessor absolute offsets must be component aligned.");
            }

            CheckAccessorRange(indexOffset, sparseCount, indexSize, indexSize, indexBytes.Length, indexSize);
            CheckAccessorRange(valueOffset, sparseCount, elementSize, elementSize, valueBytes.Length, componentSize);
            int previous = -1;
            for (int i = 0; i < sparseCount; i++)
            {
                int destination = checked((int)Component(indexBytes[(indexOffset + (i * indexSize))..], indexType, false));
                if (destination <= previous || destination >= count)
                {
                    throw new InvalidDataException("Sparse indices must be increasing and within the accessor.");
                }

                ReadElement(valueBytes.Slice(valueOffset + (i * elementSize), elementSize), values, destination * components, components, type, normalized);
                previous = destination;
            }
        }

        return new(values, count, components, type, normalized);
    }

    private sealed record AccessorValues(double[] Values, int Count, int Components, int ComponentType, bool Normalized);
}
