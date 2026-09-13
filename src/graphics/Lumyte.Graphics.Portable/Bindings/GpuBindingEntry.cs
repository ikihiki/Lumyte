namespace Lumyte.Graphics.Portable;

/// <summary>Undefined denotes an uninitialized entry, whose missing resource is diagnosed by the runtime.</summary>
public enum GpuBindingResourceKind { Undefined, Buffer, Texture, Sampler }

/// <summary>One immutable, non-owning input value. Only the property selected by Kind is active.</summary>
public readonly record struct GpuBindingEntry
{
    private GpuBindingEntry(uint binding, GpuBindingResourceKind kind)
    { Binding = binding; Kind = kind; }

    public static GpuBindingEntry Buffer(uint binding, GpuBufferRange range)
        => new(binding, GpuBindingResourceKind.Buffer) { BufferRange = range };

    public static GpuBindingEntry Texture(uint binding, GpuTextureView view)
        => new(binding, GpuBindingResourceKind.Texture) { TextureView = view };

    public static GpuBindingEntry Sampler(uint binding, GpuSamplerDescription description)
        => new(binding, GpuBindingResourceKind.Sampler) { SamplerDescription = description };

    public uint Binding { get; }
    public GpuBindingResourceKind Kind { get; }
    public GpuBufferRange BufferRange { get; private init; }
    public GpuTextureView TextureView { get; private init; }
    public GpuSamplerDescription SamplerDescription { get; private init; }
}
