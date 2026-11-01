using Lumyte.Input.Abstractions;

namespace Lumyte.Input;

/// <summary>Associates an acquired event with the physical owner resolved before overrides.</summary>
public sealed record InputScopedEvent(InputSourceEvent Input, InputWindowId? StateWindowId);
