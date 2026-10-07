using System.Buffers.Binary;
using System.Text.Json;
using Lumyte.Graphics;
using Ahjo.Wgpu.Native;
using G = Lumyte.Graphics;
namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class Sampler : GpuResource
{
    internal WGPUSamplerImpl* Handle { get; }
    internal Sampler(WgpuDevice owner, WGPUSamplerImpl* handle) : base(owner) => Handle = handle;
    protected override void ReleaseNative() => WGPU.wgpuSamplerRelease(Handle);
}
internal readonly record struct SampledPair(TextureView View, Sampler Sampler);
internal sealed class MaterialSchema
{
    internal static MaterialSchema? Parse(string? reflection)
    {
        if (reflection is null) return null;
        using var doc = JsonDocument.Parse(reflection);
        var parameters = doc.RootElement.GetProperty("parameters");
        if (parameters.GetArrayLength() != 9 || parameters[0].GetProperty("name").GetString() != "materials") return null;
        for (int i = 0; i < 9; i++)
            {
                var binding = parameters[i].GetProperty("binding");
                if (binding.GetProperty("kind").GetString() != "descriptorTableSlot" ||
                    binding.GetProperty("index").GetInt32() != i ||
                    (binding.TryGetProperty("space", out var space) && space.GetInt32() != 0)) return null;
            }
        var type = parameters[0].GetProperty("type");
        if (type.GetProperty("kind").GetString() != "resource" || type.GetProperty("baseShape").GetString() != "structuredBuffer") return null;
        var element = type.GetProperty("resultType");
        if (element.GetProperty("sizes")[0].GetProperty("value").GetInt32() != 32) return null;
        string[] names = ["baseColor", "textureSelector", "hasTexture", "padding0", "padding1"];
        int[] offsets = [0, 16, 20, 24, 28];
        var fields = element.GetProperty("fields");
        if (fields.GetArrayLength() != 5) return null;
        for (int i = 0; i < 5; i++)
            if (fields[i].GetProperty("name").GetString() != names[i] || fields[i].GetProperty("binding").GetProperty("offset").GetInt32() != offsets[i]) return null;
        var colorType = fields[0].GetProperty("type");
        if (colorType.GetProperty("kind").GetString() != "vector" || colorType.GetProperty("elementCount").GetInt32() != 4 ||
            colorType.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32") return null;
        for (int i = 1; i < 5; i++)
            if (fields[i].GetProperty("type").GetProperty("kind").GetString() != "scalar" ||
                fields[i].GetProperty("type").GetProperty("scalarType").GetString() != "uint32") return null;
        for (int i = 0; i < 4; i++)
            if (parameters[1 + i * 2].GetProperty("name").GetString() != $"materialTexture{i}" ||
                parameters[2 + i * 2].GetProperty("name").GetString() != $"materialSampler{i}" ||
                parameters[1 + i * 2].GetProperty("type").GetProperty("baseShape").GetString() != "texture2D" ||
                parameters[2 + i * 2].GetProperty("type").GetProperty("kind").GetString() != "samplerState") return null;
        return new();
    }
}
internal sealed class MaterialRegion(WgpuBuffer buffer, ulong offset, ulong length, MaterialBindings bindings)
{
    internal WgpuBuffer Buffer { get; } = buffer;
    internal ulong Offset { get; } = offset;
    internal ulong Length { get; } = length;
    internal MaterialBindings Bindings { get; } = bindings;
    internal bool Valid { get; set; } = true;
    internal bool Ready { get; set; }
    internal void Check(WgpuDevice owner)
    {
        Buffer.Check(owner); Bindings.Check(owner);
        if (!Valid || !Ready) throw new ArgumentException("Material data was overwritten or its registration was invalidated.");
    }
}
internal readonly record struct MaterialTransfer(MaterialRegion Region);
internal sealed class MaterialBindings : GpuResource, IGraphicsMaterialBindings
{
    public MaterialResourceLayout Layout { get; }
    public ulong MaterialCount { get; }
    public ulong SizeInBytes => Layout.GetSizeInBytes(MaterialCount);
    internal SampledPair[] Pairs { get; }
    private readonly byte[] _bytes;
    internal MaterialBindings(WgpuDevice owner, MaterialResourceLayout layout, ulong count, byte[] bytes, SampledPair[] pairs) : base(owner)
    {
        Layout = layout; MaterialCount = count; _bytes = bytes; Pairs = pairs;
        foreach (var pair in Pairs.Distinct()) { pair.View.Acquire(); pair.Sampler.Acquire(); }
    }
    internal static (byte[] Bytes, SampledPair[] Pairs) Prepare(WgpuDevice owner, MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials)
    {
        ArgumentNullException.ThrowIfNull(desc); ArgumentNullException.ThrowIfNull(desc.Layout);
        if (desc.Layout.Handle is not MaterialSchema) throw new ArgumentException("Invalid material layout.");
        if (materials.IsEmpty) throw new ArgumentException("At least one material is required.");
        var unique = new List<SampledPair> { Resolve(owner, desc.UnusedSlotFallback) };
        var bytes = new byte[checked(materials.Length * 32)];
        for (int i = 0; i < materials.Length; i++)
        {
            var m = materials[i]; var c = m.BaseColor;
            if (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z) || !float.IsFinite(c.W)) throw new ArgumentException("Material coefficients must be finite.");
            var row = bytes.AsSpan(i * 32, 32);
            BinaryPrimitives.WriteSingleLittleEndian(row, c.X); BinaryPrimitives.WriteSingleLittleEndian(row[4..], c.Y);
            BinaryPrimitives.WriteSingleLittleEndian(row[8..], c.Z); BinaryPrimitives.WriteSingleLittleEndian(row[12..], c.W);
            if (m.BaseColorTexture is { } reference)
            {
                var pair = Resolve(owner, reference);
                int index = unique.IndexOf(pair);
                if (index < 0) { index = unique.Count; unique.Add(pair); }
                if (unique.Count > 4) throw new NotSupportedException("This material variant supports four sampled pairs including fallback.");
                BinaryPrimitives.WriteUInt32LittleEndian(row[16..], (uint)index);
                BinaryPrimitives.WriteUInt32LittleEndian(row[20..], 1);
            }
        }
        var pairs = Enumerable.Range(0, 4).Select(i => i < unique.Count ? unique[i] : unique[0]).ToArray();
        return (bytes, pairs);
    }
    internal static SampledPair Resolve(WgpuDevice owner, SampledTexture2DReference reference)
    {
        if (reference.Handle is not SampledPair pair) throw new ArgumentException("Invalid sampled reference.");
        pair.View.Check(owner); pair.Sampler.Check(owner);
        if (!pair.View.Texture.Usage.HasFlag(TextureUsage.Sampled)) throw new ArgumentException("Sampled texture usage is required.");
        return pair;
    }
    public void CopyTo(G.BufferSlice<byte> destination)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            if (destination.Range.Buffer is not WgpuBuffer buffer) throw new ArgumentException("Unsupported buffer.");
            buffer.Check(Owner);
            if (destination.SizeInBytes != SizeInBytes) throw new ArgumentException("Material pack requires the complete exact-size range.");
            buffer.CopyFrom(_bytes, destination.OffsetInBytes, destination.SizeInBytes);
            buffer.RegisterMaterial(destination.OffsetInBytes, destination.SizeInBytes, this);
        }
    }
    protected override void ReleaseNative()
    { foreach (var pair in Pairs.Distinct()) { pair.View.ReleaseLease(); pair.Sampler.ReleaseLease(); } }
}
internal sealed unsafe class MaterialArguments : GpuResource
{
    internal GraphicsPipeline Pipeline { get; }
    internal MaterialRegion Region { get; }
    internal WGPUBindGroupImpl* Handle { get; }
    internal MaterialArguments(GraphicsPipeline pipeline, MaterialRegion region, WGPUBindGroupImpl* handle) : base(pipeline.Owner)
    { Pipeline = pipeline; Region = region; Handle = handle; pipeline.Acquire(); region.Buffer.Acquire(); region.Bindings.Acquire(); }
    protected override void ReleaseNative()
    { WGPU.wgpuBindGroupRelease(Handle); Pipeline.ReleaseLease(); Region.Buffer.ReleaseLease(); Region.Bindings.ReleaseLease(); }
}
