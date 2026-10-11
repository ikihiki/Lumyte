namespace Lumyte.Graphics.ImageEffects;

/// <summary>Applies a matrix to unpremultiplied linear RGBA.</summary>
/// <param name="Matrix">The immutable matrix.</param>
/// <param name="Input">The input operation.</param>
public sealed record ColorMatrixEffectNode(ColorMatrix4x5 Matrix, ImageEffectNode Input) : ImageEffectNode;
