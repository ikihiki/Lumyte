using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Internal prepared payload with explicit initialization and retirement ownership.</summary>
/// <remarks>
/// Recording is serialized. The default lease is exclusive; explicitly immutable builtin
/// owners may share completed payloads while retaining every consuming frame. Disposal waits
/// for every GPU completion and command retirement. Only the owner publishes residency after confirmed completion;
/// recording uploads alone does not make this asset resident.
/// </remarks>
internal interface IRetainedCommandBatch2D : IPreparedCommandBatch2D
{
    /// <summary>Gets conservative estimated private allocation and packed-payload bytes.</summary>
    long EstimatedBytes { get; }

    /// <summary>Records without the one-shot guard; resident assets skip initialization and uploads.</summary>
    /// <param name="commands">The recording command buffer.</param>
    /// <param name="target">The active drawing target.</param>
    /// <param name="resident">Whether confirmed initialization and uploads may be skipped.</param>
    void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident);

    /// <summary>Publishes completion only from the retained owner after submission and command retirement.</summary>
    void ConfirmCompletion()
    {
    }
}
