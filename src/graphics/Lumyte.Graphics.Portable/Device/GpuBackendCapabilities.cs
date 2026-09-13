namespace Lumyte.Graphics.Portable;

/// <summary>Features enabled on the created device, rather than features merely supported by its adapter.</summary>
public readonly record struct GpuBackendCapabilities(
    bool DirectRootData = false,
    bool DualSourceBlend = false,
    bool IndirectFirstInstance = false);
