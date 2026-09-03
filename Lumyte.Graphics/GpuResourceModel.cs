namespace Lumyte.Graphics;

/// <summary>Logical address into an allocation; resolvable even without native GPU pointers.</summary>
public readonly record struct GpuMemoryAddress(ulong AllocationId, ulong Offset = 0, ulong Length = 0)
{
    public static GpuMemoryAddress Null => default;
    public bool IsNull => AllocationId == 0;
    public GpuMemoryAddress Add(ulong byteOffset)
    {
        if (byteOffset > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }
        return new(AllocationId, checked(Offset + byteOffset), Length - byteOffset);
    }
}

/// <summary>A real shader-visible virtual address. Unsupported backends must not synthesize it.</summary>
public readonly record struct GpuDeviceAddress(ulong Value) { public bool IsNull => Value == 0; }

public readonly record struct GpuMemoryAllocation(
    ulong Size,
    ulong Alignment,
    GpuMemoryKind Kind,
    nint CpuAddress,
    GpuMemoryAddress MemoryAddress)
{
    public GpuMemoryAllocation Validate()
    {
        if (Size == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Size));
        }

        if (Alignment == 0 || !System.Numerics.BitOperations.IsPow2(Alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(Alignment));
        }

        if (MemoryAddress.IsNull || MemoryAddress.Length < Size || MemoryAddress.Offset % Alignment != 0)
        {
            throw new ArgumentException("Allocation address must identify an aligned region large enough for the allocation.", nameof(MemoryAddress));
        }

        if (Kind == GpuMemoryKind.DeviceLocal && CpuAddress != 0)
        {
            throw new ArgumentException("GPU-only memory cannot expose a CPU address.", nameof(CpuAddress));
        }

        if (Kind != GpuMemoryKind.DeviceLocal && CpuAddress == 0)
        {
            throw new ArgumentException("CPU-visible memory requires a CPU address.", nameof(CpuAddress));
        }

        return this;
    }

    public unsafe Span<byte> MappedBytes()
    {
        if (CpuAddress == 0)
        {
            throw new InvalidOperationException("Allocation is not CPU visible.");
        }

        return new Span<byte>((void*)CpuAddress, checked((int)Size));
    }
}

[Flags]
public enum GpuBufferUsage
{
    None = 0,
    CopySource = 1 << 0,
    CopyDestination = 1 << 1,
    ShaderData = 1 << 2,
    IndirectArguments = 1 << 3,
}

public readonly record struct GpuBufferDescription(ulong Size, GpuBufferUsage Usage)
{
    public GpuBufferDescription Validate()
    {
        if (Size == 0) { throw new ArgumentOutOfRangeException(nameof(Size)); }
        if (Usage == GpuBufferUsage.None) { throw new ArgumentOutOfRangeException(nameof(Usage)); }
        return this;
    }
}

public readonly record struct GpuBufferMemoryRequirements(ulong Size, ulong Alignment)
{
    public GpuBufferMemoryRequirements(ulong size, ulong alignment, ulong compatibility)
        : this(size, alignment) => Compatibility = compatibility;

    public ulong Compatibility { get; init; }

    public GpuBufferMemoryRequirements Validate()
    {
        if (Size == 0 || Alignment == 0 || !System.Numerics.BitOperations.IsPow2(Alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(Alignment));
        }

        return this;
    }
}

public readonly record struct GpuBufferHandle(ulong Value, ulong Size)
{
    public bool IsNull => Value == 0;
}

[Flags]
public enum GpuTextureUsage
{
    None = 0,
    Sampled = 1 << 0,
    Storage = 1 << 1,
    ColorAttachment = 1 << 2,
    DepthStencilAttachment = 1 << 3,
    CopySource = 1 << 4,
    CopyDestination = 1 << 5,
}

public readonly record struct GpuTextureDescription(
    uint Width,
    uint Height,
    GpuFormat Format,
    GpuTextureUsage Usage,
    uint MipCount = 1,
    uint LayerCount = 1,
    uint SampleCount = 1)
{
    public GpuTextureDescription Validate()
    {
        if (Width == 0 || Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }

        if (MipCount == 0 || LayerCount == 0 || SampleCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MipCount));
        }

        if (Usage == GpuTextureUsage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(Usage));
        }

        return this;
    }
}

public readonly record struct GpuTextureMemoryRequirements(ulong Size, ulong Alignment)
{
    public GpuTextureMemoryRequirements(ulong size, ulong alignment, ulong compatibility)
        : this(size, alignment) => Compatibility = compatibility;

    public ulong Compatibility { get; init; }

    public GpuTextureMemoryRequirements Validate()
    {
        if (Size == 0 || Alignment == 0 || !System.Numerics.BitOperations.IsPow2(Alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(Alignment));
        }

        return this;
    }
}

public readonly record struct GpuTextureHandle(ulong Value)
{
    public bool IsNull => Value == 0;
}

public readonly record struct GpuRasterPipelineHandle(ulong Value)
{
    public bool IsNull => Value == 0;
}

public readonly record struct GpuComputePipelineHandle(ulong Value)
{
    public bool IsNull => Value == 0;
}

/// <summary>Device-issued logical identifier for a shader-visible texture view.</summary>
public readonly record struct TextureId(ulong Value)
{
    public bool IsNull => Value == 0;
}

/// <summary>Device-issued logical identifier for a shader-visible sampler.</summary>
public readonly record struct SamplerId(ulong Value)
{
    public bool IsNull => Value == 0;
}

public enum GpuSamplerFilter { Nearest, Linear }
public enum GpuSamplerAddressMode { ClampToEdge, Repeat }

public readonly record struct GpuSamplerDescription(
    GpuSamplerFilter MinFilter = GpuSamplerFilter.Nearest,
    GpuSamplerFilter MagFilter = GpuSamplerFilter.Nearest,
    GpuSamplerAddressMode AddressU = GpuSamplerAddressMode.ClampToEdge,
    GpuSamplerAddressMode AddressV = GpuSamplerAddressMode.ClampToEdge)
{
    public GpuSamplerDescription Validate()
    {
        if (!Enum.IsDefined(MinFilter) || !Enum.IsDefined(MagFilter)
            || !Enum.IsDefined(AddressU) || !Enum.IsDefined(AddressV))
        {
            throw new ArgumentOutOfRangeException(nameof(MinFilter));
        }
        return this;
    }
}

public readonly record struct GpuTextureViewDescription(
    GpuFormat Format,
    uint BaseMip = 0,
    uint MipCount = uint.MaxValue,
    uint BaseLayer = 0,
    uint LayerCount = uint.MaxValue);

public readonly record struct GpuTextureView(
    TextureId Id,
    GpuTextureHandle Texture,
    GpuTextureViewDescription Description);

public enum GpuAttachmentLoadOperation { Load, Clear, Discard }
public enum GpuAttachmentStoreOperation { Store, Discard }

public readonly record struct GpuClearColor(float Red, float Green, float Blue, float Alpha);
public readonly record struct GpuClearDepthStencil(float Depth = 1, byte Stencil = 0);

public readonly record struct GpuColorAttachment(
    GpuTextureView View,
    GpuAttachmentLoadOperation LoadOperation,
    GpuAttachmentStoreOperation StoreOperation,
    GpuClearColor ClearColor = default);

public readonly record struct GpuDepthStencilAttachment(
    GpuTextureView View,
    GpuAttachmentLoadOperation LoadOperation,
    GpuAttachmentStoreOperation StoreOperation,
    GpuClearDepthStencil ClearValue = default);

public readonly record struct GpuTransientLifetime(int FirstPass, int LastPass)
{
    public bool Overlaps(GpuTransientLifetime other)
        => FirstPass <= other.LastPass && other.FirstPass <= LastPass;

    public GpuTransientLifetime Validate()
    {
        if (FirstPass < 0 || LastPass < FirstPass)
        {
            throw new ArgumentOutOfRangeException(nameof(FirstPass));
        }

        return this;
    }
}

public readonly record struct GpuTransientTextureRequest(
    GpuTextureDescription Texture,
    GpuTransientLifetime Lifetime);
