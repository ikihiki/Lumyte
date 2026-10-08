using System.Buffers.Binary;
using System.Numerics;
using G = Lumyte.Graphics;

namespace Lumyte.Graphics.Wgpu;

internal sealed class MaterialBindings : GpuResource, IGraphicsMaterialBindings
{
    private readonly byte[] _bytes;

    internal MaterialBindings(WgpuDevice owner, MaterialResourceLayout layout, ulong count, byte[] bytes, SampledPair[] pairs)
        : base(owner)
    {
        Layout = layout;
        MaterialCount = count;
        _bytes = bytes;
        Pairs = pairs;
        foreach (SampledPair pair in Pairs.Distinct())
        {
            pair.View.Acquire();
            pair.Sampler.Acquire();
        }
    }

    public MaterialResourceLayout Layout { get; }

    public ulong MaterialCount { get; }

    public ulong SizeInBytes => Layout.GetSizeInBytes(MaterialCount);

    internal SampledPair[] Pairs { get; }

    public void CopyTo(G.BufferSlice<byte> destination)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            if (destination.Range.Buffer is not WgpuBuffer buffer)
            {
                throw new ArgumentException("Unsupported buffer.");
            }

            buffer.Check(Owner);
            if (destination.SizeInBytes != SizeInBytes)
            {
                throw new ArgumentException("Material pack requires the complete exact-size range.");
            }

            buffer.CopyFrom(_bytes, destination.OffsetInBytes, destination.SizeInBytes);
            buffer.RegisterMaterial(destination.OffsetInBytes, destination.SizeInBytes, this);
        }
    }

    internal static (byte[] Bytes, SampledPair[] Pairs) Prepare(WgpuDevice owner, MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.Layout);
        if (desc.Layout.Handle is not MaterialSchema)
        {
            throw new ArgumentException("Invalid material layout.");
        }

        if (materials.IsEmpty)
        {
            throw new ArgumentException("At least one material is required.");
        }

        var unique = new List<SampledPair>
        {
            Resolve(owner, desc.UnusedSlotFallback),
        };
        byte[] bytes = new byte[checked(materials.Length * 32)];
        for (int i = 0; i < materials.Length; i++)
        {
            MaterialData m = materials[i];
            Vector4 c = m.BaseColor;
            if (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z) || !float.IsFinite(c.W))
            {
                throw new ArgumentException("Material coefficients must be finite.");
            }

            Span<byte> row = bytes.AsSpan(i * 32, 32);
            BinaryPrimitives.WriteSingleLittleEndian(row, c.X);
            BinaryPrimitives.WriteSingleLittleEndian(row[4..], c.Y);
            BinaryPrimitives.WriteSingleLittleEndian(row[8..], c.Z);
            BinaryPrimitives.WriteSingleLittleEndian(row[12..], c.W);
            if (m.BaseColorTexture is { } reference)
            {
                SampledPair pair = Resolve(owner, reference);
                int index = unique.IndexOf(pair);
                if (index < 0)
                {
                    index = unique.Count;
                    unique.Add(pair);
                }

                if (unique.Count > 4)
                {
                    throw new NotSupportedException("This material variant supports four sampled pairs including fallback.");
                }

                BinaryPrimitives.WriteUInt32LittleEndian(row[16..], (uint)index);
                BinaryPrimitives.WriteUInt32LittleEndian(row[20..], 1);
            }
        }

        SampledPair[] pairs = Enumerable.Range(0, 4).Select(i => i < unique.Count ? unique[i] : unique[0]).ToArray();
        return (bytes, pairs);
    }

    internal static SampledPair Resolve(WgpuDevice owner, SampledTexture2DReference reference)
    {
        if (reference.Handle is not SampledPair pair)
        {
            throw new ArgumentException("Invalid sampled reference.");
        }

        pair.View.Check(owner);
        pair.Sampler.Check(owner);
        if (!pair.View.Texture.Usage.HasFlag(TextureUsage.Sampled))
        {
            throw new ArgumentException("Sampled texture usage is required.");
        }

        return pair;
    }

    protected override void ReleaseNative()
    {
        foreach (SampledPair pair in Pairs.Distinct())
        {
            pair.View.ReleaseLease();
            pair.Sampler.ReleaseLease();
        }
    }
}
