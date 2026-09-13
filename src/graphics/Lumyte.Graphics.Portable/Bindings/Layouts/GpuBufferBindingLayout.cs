namespace Lumyte.Graphics.Portable;

public enum GpuBufferBindingType { Uniform, ReadOnlyStorage, Storage }

/// <summary>Buffer interpretation and optional dynamic offset, without a resource reference.</summary>
public readonly record struct GpuBufferBindingLayout(
    GpuBufferBindingType Type, ulong MinBindingSize = 0, bool HasDynamicOffset = false);
