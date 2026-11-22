using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Describes retained uncertain acceptance and retryable cleanup.</summary>
/// <param name="Generation">The fixed originating generation.</param>
/// <param name="QuarantinedEpochs">The number of unknown-acceptance epochs.</param>
/// <param name="PendingCleanupOwners">The number of retained cleanup owners.</param>
public sealed record PipelineRecoveryState(GraphicsDeviceGeneration Generation, int QuarantinedEpochs, int PendingCleanupOwners);
