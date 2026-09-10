namespace Lumyte.Graphics.Native;

/// <summary>Enabled native features. A GPU address alone does not imply raw shader pointer support.</summary>
public readonly record struct NativeGpuCapabilities(
    bool RawShaderPointers = false,
    bool BufferDescriptors = false,
    bool ExplicitTextureTransitions = false,
    bool MeshShaders = false,
    bool AmplificationShaders = false);
