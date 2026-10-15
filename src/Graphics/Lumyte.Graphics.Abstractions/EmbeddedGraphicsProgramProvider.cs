using System.Reflection;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Creates a fresh program from embedded artifacts only when explicitly acquired.</summary>
/// <param name="assembly">The assembly containing both artifacts.</param>
/// <param name="programId">The supported package-defined ABI identifier.</param>
/// <param name="vertexResourceName">The vertex artifact's manifest resource name.</param>
/// <param name="fragmentResourceName">The fragment artifact's manifest resource name.</param>
/// <remarks>Construction performs no loading or GPU allocation. External providers can instead share preloaded resources.</remarks>
public sealed class EmbeddedGraphicsProgramProvider(Assembly assembly, string programId, string vertexResourceName, string fragmentResourceName) : IGraphicsProgramProvider
{
    private readonly Assembly _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    private readonly string _programId = Required(programId);
    private readonly string _vertexResourceName = Required(vertexResourceName);
    private readonly string _fragmentResourceName = Required(fragmentResourceName);

    /// <inheritdoc/>
    public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!string.Equals(_programId, programId, StringComparison.Ordinal))
        {
            throw new NotSupportedException("This provider does not supply the requested program ABI.");
        }

        return GraphicsProgramLease.Create(device, ShaderArtifact.LoadEmbedded(_assembly, _vertexResourceName), ShaderArtifact.LoadEmbedded(_assembly, _fragmentResourceName));
    }

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
