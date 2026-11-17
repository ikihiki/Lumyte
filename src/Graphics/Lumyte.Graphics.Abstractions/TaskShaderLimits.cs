namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains enabled task stage limits.</summary>
public sealed record TaskShaderLimits
{
    /// <summary>Gets local size and dispatch limits.</summary>
    public required ShaderWorkgroupLimits Workgroup { get; init; }

    /// <summary>Gets the maximum shared storage in bytes.</summary>
    public ulong MaxSharedMemoryBytes { get; init; }

    /// <summary>Gets the maximum payload storage in bytes.</summary>
    public ulong MaxPayloadBytes { get; init; }

    /// <summary>Gets the combined payload and shared storage limit in bytes.</summary>
    public ulong MaxPayloadAndSharedMemoryBytes { get; init; }
}
