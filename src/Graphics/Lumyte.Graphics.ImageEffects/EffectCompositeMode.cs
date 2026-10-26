namespace Lumyte.Graphics.ImageEffects;

/// <summary>Specifies premultiplied-linear image composition.</summary>
public enum EffectCompositeMode
{
    /// <summary>Places the source over the backdrop.</summary>
    SrcOver,

    /// <summary>Adds RGB and clamps the summed alpha to one.</summary>
    Plus,
}
