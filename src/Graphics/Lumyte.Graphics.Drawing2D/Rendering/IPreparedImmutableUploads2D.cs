using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Publishes immutable native payloads from a validated retained-frame completion.</summary>
internal interface IPreparedImmutableUploads2D
{
    /// <summary>Publishes only after actual completion and disposed recorded command memory.</summary>
    /// <param name="submission">The actual retained frame submission.</param>
    void RetireImmutableUploads(IGraphicsSubmission submission);
}
