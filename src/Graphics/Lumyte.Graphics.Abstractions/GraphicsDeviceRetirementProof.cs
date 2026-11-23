namespace Lumyte.Graphics.Abstractions;

/// <summary>Represents trusted confirmation of safe generation retirement.</summary>
public sealed class GraphicsDeviceRetirementProof
{
    private GraphicsDeviceRetirementProof(GraphicsDeviceGeneration generation)
    {
        Generation = generation;
    }

    /// <summary>Gets the exact retired generation.</summary>
    public GraphicsDeviceGeneration Generation { get; }

    internal static GraphicsDeviceRetirementProof Issue(GraphicsDeviceGeneration generation, object issuer)
    {
        generation.ValidateIssuer(issuer);
        return new(generation);
    }

    internal void ValidateFor(IGraphicsDeviceRetirementAuthority authority, IGraphicDevice device, GraphicsDeviceGeneration generation)
    {
        generation.ValidateDevice(device);
        generation.ValidateAuthority(authority);
        if (!ReferenceEquals(Generation, generation))
        {
            throw new ArgumentException("The proof belongs to another generation.", nameof(generation));
        }
    }
}
