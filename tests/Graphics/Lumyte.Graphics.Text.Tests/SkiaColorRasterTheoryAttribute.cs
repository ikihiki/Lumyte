using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Runs comparison bitmap cases only on their supported Skia FreeType platforms.</summary>
internal sealed class SkiaColorRasterTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SkiaColorRasterTheoryAttribute"/> class.</summary>
    public SkiaColorRasterTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsBrowser())
        {
            Skip = "The legacy Skia bitmap oracle requires the bundled FreeType backend on Linux/Browser; vector COLR tests run on every platform.";
        }
    }
}
