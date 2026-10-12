namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>A detached resource lifetime inspection result.</summary>
/// <param name="Name">The Name value.</param>
/// <param name="Kind">The Kind value.</param>
/// <param name="Transient">The Transient value.</param>
/// <param name="Output">The Output value.</param>
/// <param name="FirstPass">The FirstPass value.</param>
/// <param name="LastPass">The LastPass value.</param>
public sealed record RenderGraphResourceDiagnostic(string Name, string Kind, bool Transient, bool Output, long FirstPass, long LastPass);
