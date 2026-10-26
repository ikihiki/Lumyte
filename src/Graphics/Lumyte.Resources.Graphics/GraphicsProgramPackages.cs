namespace Lumyte.Resources.Graphics;

/// <summary>Freezes the logical program addresses and their shader input definitions for one manager.</summary>
public sealed class GraphicsProgramPackages
{
    private readonly Dictionary<ResourceAddress, GraphicsProgramPackage> _packages;

    /// <summary>Initializes a new instance of the <see cref="GraphicsProgramPackages"/> class.</summary>
    /// <param name="packages">The program keys and their externally configured artifact sources.</param>
    public GraphicsProgramPackages(IReadOnlyDictionary<ResourceKey<GraphicsProgramResource>, GraphicsProgramPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        _packages = [];
        foreach ((ResourceKey<GraphicsProgramResource> key, GraphicsProgramPackage package) in packages)
        {
            ArgumentNullException.ThrowIfNull(package);
            _packages.Add(key.Address, package);
        }
    }

    internal GraphicsProgramPackage Get(ResourceAddress address)
        => _packages.TryGetValue(address, out GraphicsProgramPackage? package)
            ? package : throw new InvalidOperationException("No shader inputs are registered for this program address.");
}
