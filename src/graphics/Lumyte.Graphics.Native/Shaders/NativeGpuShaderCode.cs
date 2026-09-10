namespace Lumyte.Graphics.Native;

/// <summary>Raw device-specific shader code. The caller keeps the bytes stable through pipeline creation.</summary>
public sealed record NativeGpuShaderCode
{
    public required GpuShaderStage Stage { get; init; }
    public required ReadOnlyMemory<byte> Code { get; init; }

    /// <summary>Selects a SPIR-V entry. DXIL already contains the compiled entry and treats this as metadata.</summary>
    public string EntryPoint { get; init; } = "main";
}
