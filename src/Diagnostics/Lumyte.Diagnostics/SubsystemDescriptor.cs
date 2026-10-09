namespace Lumyte.Diagnostics;

/// <summary>A versioned operation subsystem.</summary>
/// <param name="Id">The Id argument.</param>
/// <param name="DisplayName">The DisplayName argument.</param>
/// <param name="SchemaVersion">The SchemaVersion argument.</param>
public sealed record SubsystemDescriptor(string Id, string DisplayName, int SchemaVersion);
