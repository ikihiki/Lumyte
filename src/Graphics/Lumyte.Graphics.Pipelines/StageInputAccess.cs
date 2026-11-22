using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Declares the complete device access to one stage input.</summary>
public sealed record StageInputAccess
{
    /// <summary>Initializes a new instance of the <see cref="StageInputAccess"/> class.</summary>
    /// <param name="inputIndex">The zero-based input index, at most five.</param>
    /// <param name="access">The complete nonempty device access set.</param>
    public StageInputAccess(int inputIndex, ResourceAccess access)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputIndex, 5);
        const ResourceAccess Allowed = ResourceAccess.CopyRead | ResourceAccess.CopyWrite | ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.IndexRead | ResourceAccess.IndirectRead | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite;
        if (access == ResourceAccess.None || (access & ~Allowed) != 0)
        {
            throw new ArgumentException("Declare a nonempty valid device access set.", nameof(access));
        }

        InputIndex = inputIndex;
        Access = access;
    }

    /// <summary>Gets the input index.</summary>
    public int InputIndex { get; }

    /// <summary>Gets the complete device access set.</summary>
    public ResourceAccess Access { get; }
}
