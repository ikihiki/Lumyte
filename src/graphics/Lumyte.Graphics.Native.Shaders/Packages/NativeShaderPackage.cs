namespace Lumyte.Graphics.Native.Shaders;

/// <summary>Expanded CPU data supplied by the resource layer. This type does not read a file or container.</summary>
public sealed class NativeShaderPackage
{
    public const uint CurrentVersion = 1;

    public NativeShaderPackage(uint version, IEnumerable<NativeShaderArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        NativeShaderArtifact[] snapshot = artifacts.ToArray();
        if (snapshot.Any(static artifact => artifact is null))
        {
            throw new ArgumentException("A package artifact cannot be null.", nameof(artifacts));
        }
        if (snapshot.Length > 1 && snapshot.Any(artifact => artifact.PrimaryStage != snapshot[0].PrimaryStage))
        {
            throw new ArgumentException("A package describes one compute, vertex raster, or mesh raster program family.", nameof(artifacts));
        }
        Version = version;
        Artifacts = Array.AsReadOnly(snapshot);
    }

    public uint Version { get; }
    public IReadOnlyList<NativeShaderArtifact> Artifacts { get; }
}
