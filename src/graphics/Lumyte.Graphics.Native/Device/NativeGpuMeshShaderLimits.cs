namespace Lumyte.Graphics.Native;

/// <summary>Enabled mesh-stage limits, distinct from compute dispatch and shader-local thread counts.</summary>
/// <remarks>
/// AmplificationDispatch is absent when amplification is unavailable, in which case MaxPayloadSize is zero.
/// Output vertex and primitive limits apply to one mesh workgroup.
/// Payload size is in bytes. Native shader rules still constrain combinations of payload, shared memory, and output.
/// </remarks>
public readonly record struct NativeGpuMeshShaderLimits(
    NativeGpuDispatchLimits MeshDispatch,
    NativeGpuDispatchLimits? AmplificationDispatch,
    uint MaxOutputVertices,
    uint MaxOutputPrimitives,
    uint MaxPayloadSize);
