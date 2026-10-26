namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes the planned lifetime of an allocation without retaining it.</summary>
/// <param name="Name">The diagnostic name.</param>
/// <param name="Kind">Texture, Buffer or ShaderDataBuffer.</param>
/// <param name="Transient">Whether the plan owns the allocation.</param>
/// <param name="Output">Whether explicitly retained as an output.</param>
/// <param name="FirstPass">The first live use, or -1 if unused.</param>
/// <param name="LastPass">The last live use, or -1 if unused.</param>
public sealed record RenderGraphResourceInfo(string Name, string Kind, bool Transient, bool Output, int FirstPass, int LastPass);
