namespace Lumyte.UI;

/// <summary>Owns animation connections for one persistent UI instance.</summary>
public partial class UiElement
{
    internal UiSurface.AnimationLease? Animations { get; set; }
}
