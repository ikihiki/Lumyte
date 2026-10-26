namespace Lumyte.Graphics.ImageEffects;

/// <summary>Identifies the interpretation of linear-sRGB-primary image values.</summary>
public enum EffectColorSemantic
{
    /// <summary>Display-referred linear values.</summary>
    DisplayLinear,

    /// <summary>Scene-referred linear values before tone mapping.</summary>
    SceneLinear,
}
