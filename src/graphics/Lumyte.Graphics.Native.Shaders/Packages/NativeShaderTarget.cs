namespace Lumyte.Graphics.Native.Shaders;

/// <summary>The Native artifact target, independent of the backend's implementation assembly.</summary>
public enum NativeShaderTarget
{
    DirectX12,
    Vulkan,
}

/// <summary>Shader features required to select an artifact. These do not request features from a device.</summary>
[Flags]
public enum NativeShaderCapabilities
{
    None = 0,
    RawShaderPointers = 1,
    BufferDescriptors = 2,
    MeshShaders = 4,
    AmplificationShaders = 8,
}
