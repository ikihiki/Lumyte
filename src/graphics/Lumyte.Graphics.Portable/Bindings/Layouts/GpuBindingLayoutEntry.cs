namespace Lumyte.Graphics.Portable;

/// <summary>Undefined denotes an uninitialized entry, whose missing layout is diagnosed by the runtime.</summary>
public enum GpuBindingLayoutKind { Undefined, Buffer, Texture, StorageTexture, Sampler }

/// <summary>One immutable declaration in a group. The selected kind identifies its active layout value.</summary>
public readonly record struct GpuBindingLayoutEntry
{
    private GpuBindingLayoutEntry(uint binding, GpuShaderStage visibility, GpuBindingLayoutKind kind)
    { Binding = binding; Visibility = visibility; Kind = kind; }

    public GpuBindingLayoutEntry(uint binding, GpuShaderStage visibility, GpuBufferBindingLayout layout)
        : this(binding, visibility, GpuBindingLayoutKind.Buffer) { BufferLayout = layout; }

    public GpuBindingLayoutEntry(uint binding, GpuShaderStage visibility, GpuTextureBindingLayout layout)
        : this(binding, visibility, GpuBindingLayoutKind.Texture) { TextureLayout = layout; }

    public GpuBindingLayoutEntry(uint binding, GpuShaderStage visibility, GpuStorageTextureBindingLayout layout)
        : this(binding, visibility, GpuBindingLayoutKind.StorageTexture) { StorageTextureLayout = layout; }

    public GpuBindingLayoutEntry(uint binding, GpuShaderStage visibility, GpuSamplerBindingLayout layout)
        : this(binding, visibility, GpuBindingLayoutKind.Sampler) { SamplerLayout = layout; }

    public uint Binding { get; }
    public GpuShaderStage Visibility { get; }
    public GpuBindingLayoutKind Kind { get; }
    public GpuBufferBindingLayout BufferLayout { get; }
    public GpuTextureBindingLayout TextureLayout { get; }
    public GpuStorageTextureBindingLayout StorageTextureLayout { get; }
    public GpuSamplerBindingLayout SamplerLayout { get; }
}
