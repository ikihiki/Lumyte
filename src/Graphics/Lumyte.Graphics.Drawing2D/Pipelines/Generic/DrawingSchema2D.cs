using System.Runtime.CompilerServices;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal static class DrawingSchema2D
{
    internal static SchemaDescriptor Make<T>(string name, bool dependencyOnly = false, string resourceRole = "value")
        where T : unmanaged
        => new(1, typeof(T), Unsafe.SizeOf<T>(), new Dictionary<string, object>
        {
            ["name"] = name,
            ["dependencyOnly"] = dependencyOnly,
            ["resourceRole"] = resourceRole,
            ["coordinates"] = "local geometry; inverse pixel-to-local affine",
            ["color"] = "linear-premultiplied",
            ["samples"] = 16,
            ["gridVersion"] = 1,
            ["sampleOrder"] = "s=4*y+x; offset=((x+.5)/4,(y+.5)/4)",
            ["meshCoverage"] = "inverse pixel-center barycentric saturate max; broadcast16",
            ["composite"] = "per-command correlated mean16 then painter-order SrcOver",
        });
}
