namespace Lumyte.Worlds;

/// <summary>The committed execution permissions of a world.</summary>
/// <param name="UpdateEnabled">Whether simulation may update.</param>
/// <param name="DrawEnabled">Whether drawing is enabled.</param>
/// <param name="InputEnabled">Whether input may be dispatched.</param>
public readonly record struct WorldExecutionState(bool UpdateEnabled, bool DrawEnabled, bool InputEnabled);
