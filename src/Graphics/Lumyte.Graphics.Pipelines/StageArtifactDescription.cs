using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Captures target and SHA-256 identities of ordered program modules.</summary>
public sealed record StageArtifactDescription
{
    /// <summary>Initializes a new instance of the <see cref="StageArtifactDescription"/> class.</summary>
    /// <param name="key">The implementation identity.</param>
    /// <param name="backendTarget">The exact ShaderTarget name.</param>
    /// <param name="reflectedAbiHash">Uppercase SHA-256 of ordered length-prefixed UTF-8 reflection documents.</param>
    /// <param name="programHash">Uppercase SHA-256 of ordered length-prefixed complete shader binaries.</param>
    public StageArtifactDescription(StageArtifactKey key, string backendTarget, string reflectedAbiHash, string programHash)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enum.TryParse(backendTarget, false, out ShaderTarget target) || !Enum.IsDefined(target) || target.ToString() != backendTarget)
        {
            throw new ArgumentException("Use an exact supported ShaderTarget name.", nameof(backendTarget));
        }

        ValidateHash(reflectedAbiHash);
        ValidateHash(programHash);
        Key = key;
        BackendTarget = backendTarget;
        ReflectedAbiHash = reflectedAbiHash;
        ProgramHash = programHash;
    }

    /// <summary>Gets the implementation identity.</summary>
    public StageArtifactKey Key { get; }

    /// <summary>Gets the exact target name.</summary>
    public string BackendTarget { get; }

    /// <summary>Gets the reflected ABI identity.</summary>
    public string ReflectedAbiHash { get; }

    /// <summary>Gets the complete program identity.</summary>
    public string ProgramHash { get; }

    private static void ValidateHash(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException("Expected an uppercase SHA-256 identity.", nameof(hash));
        }
    }
}
