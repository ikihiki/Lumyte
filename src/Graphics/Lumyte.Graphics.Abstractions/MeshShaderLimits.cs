namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains enabled mesh stage limits.</summary>
public sealed record MeshShaderLimits
{
    /// <summary>Gets local size and dispatch limits.</summary>
    public required ShaderWorkgroupLimits Workgroup { get; init; }

    /// <summary>Gets the maximum output vertex count.</summary>
    public uint MaxOutputVertices { get; init; }

    /// <summary>Gets the maximum output primitive count.</summary>
    public uint MaxOutputPrimitives { get; init; }

    /// <summary>Gets the maximum shared storage in bytes.</summary>
    public ulong MaxSharedMemoryBytes { get; init; }

    /// <summary>Gets the maximum payload storage in bytes.</summary>
    public ulong MaxPayloadBytes { get; init; }

    /// <summary>Gets the combined payload and shared storage limit in bytes.</summary>
    public ulong MaxPayloadAndSharedMemoryBytes { get; init; }

    /// <summary>Gets the maximum output storage in bytes.</summary>
    public ulong MaxOutputMemoryBytes { get; init; }

    /// <summary>Gets the combined payload and output storage limit in bytes.</summary>
    public ulong MaxPayloadAndOutputMemoryBytes { get; init; }
}
