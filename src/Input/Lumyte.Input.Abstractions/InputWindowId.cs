namespace Lumyte.Input.Abstractions;

/// <summary>Identifies one window connection lifetime, independently of OS handles.</summary>
public readonly record struct InputWindowId(Guid Value);
