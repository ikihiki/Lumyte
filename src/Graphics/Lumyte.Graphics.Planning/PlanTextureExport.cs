using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>An observable immutable image version and its explicit interop release layout.</summary>
/// <param name="Texture">The exact exported version and view range.</param>
/// <param name="FinalState">The required final layout, including explicit presentation release.</param>
/// <param name="FinalScope">The receiving external access scope.</param>
public sealed record PlanTextureExport(PlanTexture Texture, TextureState FinalState, BarrierScope FinalScope);
