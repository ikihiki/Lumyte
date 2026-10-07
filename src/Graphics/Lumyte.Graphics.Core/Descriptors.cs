namespace Lumyte.Graphics;

[Flags]
public enum BufferUsage { CopySource = 1, CopyDestination = 2, ShaderRead = 4, ShaderWrite = 8, Index = 16 }
public enum MemoryPreference { Automatic, Readback, Upload }
public enum LoadOp { Clear, Load }
public enum StoreOp { Store, Discard }
public enum IndexFormat { Uint16, Uint32 }

public sealed record BufferDesc<T> where T : unmanaged
{
    public required ulong Count { get; init; }
    public ulong SizeInBytes => checked(Count * (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
    public required BufferUsage Usage { get; init; }
    public MemoryPreference Memory { get; init; }
}

// The initial backend supports offscreen, single-sample RGBA8 targets only.
public sealed record TextureDesc
{
    public required uint Width { get; init; }
    public required uint Height { get; init; }
}

public readonly record struct Color4(double R, double G, double B, double A);
public sealed record RenderPassDesc
{
    public required TextureView Target { get; init; }
    public LoadOp Load { get; init; } = LoadOp.Clear;
    public StoreOp Store { get; init; } = StoreOp.Store;
    public Color4 ClearValue { get; init; }
}

public sealed record GraphicsPipelineDesc
{
    public required ShaderModule Shader { get; init; }
    public string VertexEntry { get; init; } = "vertexMain";
    public string FragmentEntry { get; init; } = "fragmentMain";
}

public sealed record ComputePipelineDesc
{
    public required ShaderModule Shader { get; init; }
    public string EntryPoint { get; init; } = "computeMain";
}

public sealed record DrawDesc
{
    public required uint VertexCount { get; init; }
    public uint InstanceCount { get; init; } = 1;
    public uint FirstVertex { get; init; }
    public uint FirstInstance { get; init; }
}

public sealed record IndexedDrawDesc
{
    public required uint IndexCount { get; init; }
    public uint InstanceCount { get; init; } = 1;
    public uint FirstIndex { get; init; }
    public int BaseVertex { get; init; }
    public uint FirstInstance { get; init; }
}

public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth = 0, float MaxDepth = 1);
public readonly record struct Scissor(uint X, uint Y, uint Width, uint Height);

/// <summary>A non-owning data reference. No native address, binding slot or serialization API is exposed.</summary>
public readonly struct GpuReference<T> where T : unmanaged
{
    internal object? Handle { get; }
    internal GpuReference(object handle) => Handle = handle;
    public override string ToString() => $"GpuReference<{typeof(T).Name}>";
}
