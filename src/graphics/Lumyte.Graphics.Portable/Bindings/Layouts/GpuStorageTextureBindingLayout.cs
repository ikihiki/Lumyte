namespace Lumyte.Graphics.Portable;

public enum GpuStorageTextureAccess { ReadOnly, WriteOnly, ReadWrite }

public readonly record struct GpuStorageTextureBindingLayout(
    GpuStorageTextureAccess Access, GpuFormat Format,
    GpuTextureViewDimension ViewDimension = GpuTextureViewDimension.Texture2D);
