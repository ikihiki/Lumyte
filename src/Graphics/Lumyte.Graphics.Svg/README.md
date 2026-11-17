# Lumyte.Graphics.Svg

Initial strict static SVG adapter using pinned usvg 0.45.1 and the existing DrawingCanvas2D path/paint commands. No raster-only fallback.

```csharp
using Lumyte.Graphics.Svg;
var svg = SvgDocument2D.Parse(svgUtf8Bytes);
canvas.Begin();
svg.Draw(canvas);
var drawing = canvas.End();
```

Supports solid fills/strokes, fill rules, affine transforms, viewBox, basic shapes, curves, internal use and usvg-resolved CSS. The input must be uncompressed UTF-8 (maximum 4 MiB). No external resources are loaded. Unsupported normalized features throw FormatException; upstream may already discard invalid/unknown SVG features. See the [Japanese decision and complete exclusions](../../../docs/adr/graphics/GRAPHICS-0039-static-svg-usvg.md).

Native desktop source builds use the repository Fallout/RustToolchain workflow and copy the platform library and notices to output/publish. Browser and NuGet packaging are not supported. Cross deployment requires a tested target library plus its notices, LumyteBuildUsvgNative=false, and both LumyteUsvgNativeOutput (including notices) and LumyteUsvgNativePath (explicit target library) overrides. Missing libraries remain explicit .NET loader errors.
