namespace Lumyte.Graphics.Shared;

/// <summary>Identifies the registered range used by a native storage binding.</summary>
/// <param name="Resource">The backend allocation.</param>
/// <param name="OffsetInBytes">The original registered byte offset.</param>
/// <param name="SizeInBytes">The original registered byte length.</param>
public readonly record struct ShaderBufferBinding(object Resource, ulong OffsetInBytes, ulong SizeInBytes);
