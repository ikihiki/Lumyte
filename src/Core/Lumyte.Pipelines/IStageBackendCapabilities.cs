namespace Lumyte.Pipelines;

/// <summary>A genuine host-supplied invocation capability authorizing opaque declaration access.</summary>
/// <remarks>Implementing this interface alone does not grant access: the exact instance must be bound by the explicit backend.</remarks>
public interface IStageBackendCapabilities : IStageCapabilities, IStageBackendValueValidation;
