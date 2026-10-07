using System.Numerics;
namespace Lumyte.Graphics;

public enum FilterMode { Nearest, Linear }
public enum AddressMode { ClampToEdge, Repeat, MirrorRepeat }
public sealed record SamplerDesc
{
    public FilterMode MinFilter { get; init; } = FilterMode.Linear;
    public FilterMode MagFilter { get; init; } = FilterMode.Linear;
    public AddressMode AddressU { get; init; } = AddressMode.Repeat;
    public AddressMode AddressV { get; init; } = AddressMode.Repeat;
}
public sealed class Sampler : GpuResource
{
    internal Sampler(IGraphicsDriver driver, object handle) : base(driver, handle) { }
}
/// <summary>Non-owning sampled pair; no binding index or native handle is exposed.</summary>
public readonly struct SampledTexture2DReference
{
    internal object? Handle { get; }
    internal SampledTexture2DReference(object handle) => Handle = handle;
}
/// <summary>Initial logical schema. Values are serialized, never copied as a C# struct.</summary>
public readonly record struct MaterialData(Vector4 BaseColor, SampledTexture2DReference? BaseColorTexture = null);
public sealed record MaterialBindingsDesc
{
    public required MaterialResourceLayout Layout { get; init; }
    public required SampledTexture2DReference UnusedSlotFallback { get; init; }
}
/// <summary>Fixed ABI validated against offline Slang reflection, shared with the shader program.</summary>
public sealed class MaterialResourceLayout
{
    internal object Handle { get; }
    internal MaterialResourceLayout(object handle) => Handle = handle;
    public uint PairCapacity => 4;
    public ulong GetSizeInBytes(ulong count) => count != 0 ? checked(count * 32) : throw new ArgumentOutOfRangeException(nameof(count));
}
public interface IGraphicsMaterialBindings : IDisposable
{
    MaterialResourceLayout Layout { get; }
    ulong MaterialCount { get; }
    ulong SizeInBytes { get; }
    /// <summary>Serializes the complete snapshot into an idle Upload range. No GPU work.</summary>
    void CopyTo(BufferSlice<byte> destination);
}
public static class MaterialDataTransfer
{
    public static void CopyFrom(this BufferSlice<byte> destination, IGraphicsMaterialBindings materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        materials.CopyTo(destination);
    }
}
/// <summary>A non-owning reference to a completed material upload and its binding set.</summary>
public readonly struct MaterialBufferReference
{
    internal object? Handle { get; }
    internal MaterialBufferReference(object handle) => Handle = handle;
}
