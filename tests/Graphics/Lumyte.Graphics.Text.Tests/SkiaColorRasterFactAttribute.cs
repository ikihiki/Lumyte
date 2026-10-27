using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Runs a comparison bitmap oracle only on its supported Skia FreeType platforms.</summary>
internal sealed class SkiaColorRasterFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SkiaColorRasterFactAttribute"/> class.</summary>
    public SkiaColorRasterFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsBrowser())
        {
            Skip = "The legacy Skia bitmap oracle requires the bundled FreeType backend on Linux/Browser; vector COLR tests run on every platform.";
        }
    }
}
