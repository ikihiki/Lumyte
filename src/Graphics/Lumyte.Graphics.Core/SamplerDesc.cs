namespace Lumyte.Graphics;
/// <summary>
/// Describes immutable filtering and D2 address modes.
/// </summary>
public sealed record SamplerDesc
{
    /// <summary>
    /// Gets minification filtering; defaults to linear.
    /// </summary>
    public FilterMode MinFilter { get; init; } = FilterMode.Linear;

    /// <summary>
    /// Gets magnification filtering; defaults to linear.
    /// </summary>
    public FilterMode MagFilter { get; init; } = FilterMode.Linear;

    /// <summary>
    /// Gets the horizontal address mode; defaults to repeat.
    /// </summary>
    public AddressMode AddressU { get; init; } = AddressMode.Repeat;

    /// <summary>
    /// Gets the vertical address mode; defaults to repeat.
    /// </summary>
    public AddressMode AddressV { get; init; } = AddressMode.Repeat;
}
