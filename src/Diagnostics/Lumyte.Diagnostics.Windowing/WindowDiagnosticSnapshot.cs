namespace Lumyte.Diagnostics.Windowing;

/// <summary>A detached scalar snapshot of one managed window.</summary>
public sealed record WindowDiagnosticSnapshot(
    string System,
    string Window,
    string Parent,
    string PresentationRoot,
    string Kind,
    string Role,
    string Ownership,
    string NativeWindow,
    double X,
    double Y,
    double Width,
    double Height,
    double ClientWidth,
    double ClientHeight,
    long PixelWidth,
    long PixelHeight,
    double ScaleX,
    double ScaleY,
    bool IsVisible,
    bool IsEffectivelyVisible,
    string Lifetime);
