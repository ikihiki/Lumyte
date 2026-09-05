using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Text;

[StructLayout(LayoutKind.Sequential)]
internal struct HarfBuzzColorLayer
{
    internal uint GlyphId;
    internal uint ColorIndex;
}
