using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A typed preparation and recording contract.</summary>
internal interface IIndexBinding
{
    /// <summary>Gets the exact native index format used by the prepared variant.</summary>
    IndexFormat Format { get; }

    /// <summary>Resolves native bindings during preparation.</summary>
    /// <param name="resources">The prepared resources.</param>
    void Prepare(PreparedResources resources);

    /// <summary>Records the prepared operation.</summary>
    /// <param name="encoder">The native encoder.</param>
    void Record(IRenderEncoder encoder);
}
