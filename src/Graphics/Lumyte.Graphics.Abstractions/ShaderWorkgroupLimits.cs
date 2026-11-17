namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains positive enabled workgroup size and dispatch limits.</summary>
public sealed record ShaderWorkgroupLimits
{
    /// <summary>Gets the maximum local X size.</summary>
    public uint MaxSizeX { get; init; }

    /// <summary>Gets the maximum local Y size.</summary>
    public uint MaxSizeY { get; init; }

    /// <summary>Gets the maximum local Z size.</summary>
    public uint MaxSizeZ { get; init; }

    /// <summary>Gets the maximum local invocation product.</summary>
    public uint MaxInvocations { get; init; }

    /// <summary>Gets the maximum dispatch X count.</summary>
    public uint MaxGroupCountX { get; init; }

    /// <summary>Gets the maximum dispatch Y count.</summary>
    public uint MaxGroupCountY { get; init; }

    /// <summary>Gets the maximum dispatch Z count.</summary>
    public uint MaxGroupCountZ { get; init; }

    /// <summary>Gets the maximum dispatch count product.</summary>
    public ulong MaxTotalGroupCount { get; init; }
}
