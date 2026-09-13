namespace Lumyte.Graphics.Native.Shaders;

/// <summary>One compiled stage. The constructor owns a copy of the caller's code bytes.</summary>
public sealed class NativeShaderStageArtifact
{
    private readonly byte[] code;

    public NativeShaderStageArtifact(GpuShaderStage stage, string entryPoint, ReadOnlySpan<byte> code)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        Stage = stage;
        EntryPoint = entryPoint;
        this.code = code.ToArray();
    }

    public GpuShaderStage Stage { get; }
    public string EntryPoint { get; }

    /// <summary>Reads owned bytes without allocating or exposing array-backed Memory.</summary>
    public ReadOnlySpan<byte> Code => code;

    internal NativeGpuShaderCode BorrowCode() => new() { Stage = Stage, EntryPoint = EntryPoint, Code = code };
    internal NativeGpuShaderCode CopyCode() => new() { Stage = Stage, EntryPoint = EntryPoint, Code = code.ToArray() };
}
