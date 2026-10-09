namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains a finite color clear value.</summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
/// <param name="Alpha">The alpha channel.</param>
public readonly record struct ClearColor(double Red, double Green, double Blue, double Alpha);
