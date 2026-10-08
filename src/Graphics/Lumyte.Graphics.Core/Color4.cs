namespace Lumyte.Graphics;
/// <summary>
/// Stores a render clear color in red, green, blue, and alpha order.
/// </summary>
/// <param name="R">The red component.</param>
/// <param name="G">The green component.</param>
/// <param name="B">The blue component.</param>
/// <param name="A">The alpha component.</param>
public readonly record struct Color4(double R, double G, double B, double A);
