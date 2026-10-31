namespace Lumyte.Graphics.Direct3D12;

/// <summary>Selects an explicit DXGI adapter or the WARP software adapter.</summary>
public sealed record Direct3D12DeviceDesc
{
    /// <summary>Gets the zero-based DXGI adapter index; software adapters are rejected.</summary>
    public uint AdapterIndex { get; init; }

    /// <summary>Gets a value indicating whether to select WARP instead of a hardware adapter.</summary>
    public bool UseWarp { get; init; }

    /// <summary>Gets a value indicating whether to enable the bundled Agility debug layer and DXGI debugging.</summary>
    public bool EnableDebugLayer { get; init; }

    /// <summary>Gets a value indicating whether requires Preview Partial Graphics Programs and a supporting driver; unsupported devices are rejected.</summary>
    public bool EnablePartialGraphicsPrograms { get; init; }
}
