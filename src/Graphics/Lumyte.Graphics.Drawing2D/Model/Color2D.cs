namespace Lumyte.Graphics.Drawing2D;

/// <summary>A straight-alpha sRGB color whose components must be finite and between zero and one.</summary>
/// <param name="R">The R component.</param>
/// <param name="G">The G component.</param>
/// <param name="B">The B component.</param>
/// <param name="A">The A component.</param>
public readonly record struct Color2D(float R, float G, float B, float A)
{
    private readonly float _r = Validate(R, nameof(R));
    private readonly float _g = Validate(G, nameof(G));
    private readonly float _b = Validate(B, nameof(B));
    private readonly float _a = Validate(A, nameof(A));

    /// <summary>Gets or initializes the finite red component between zero and one.</summary>
    public float R
    {
        get => _r;
        init => _r = Validate(value, nameof(R));
    }

    /// <summary>Gets or initializes the finite green component between zero and one.</summary>
    public float G
    {
        get => _g;
        init => _g = Validate(value, nameof(G));
    }

    /// <summary>Gets or initializes the finite blue component between zero and one.</summary>
    public float B
    {
        get => _b;
        init => _b = Validate(value, nameof(B));
    }

    /// <summary>Gets or initializes the finite alpha component between zero and one.</summary>
    public float A
    {
        get => _a;
        init => _a = Validate(value, nameof(A));
    }

    private static float Validate(float value, string name)
    {
        ModelValidation2D.Unit(value, name);
        return value;
    }
}
