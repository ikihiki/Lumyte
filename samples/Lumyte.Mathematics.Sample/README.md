# Public mathematics consumer

An ordinary console project references Mathematics without friend-assembly access. It exercises the public `Geometry2D` / `Geometry3D` entry points, generic integer/decimal/BigInteger geometry, float BCL versus double FMA arithmetic, streaming struct sinks, and SIMD span operations. It also exercises explicit `Gray<byte>`, `Gray<double>`, `GrayAlpha<decimal>`, `Rgb<int>`, `Bgra<double>` layouts, `Rgb565` packed bits, typed component/packed batches, `Hsla<T>` / `Hsva<T>`, sRGB transfer and alpha association. Raw alpha is supplied explicitly; UNORM normalization adds opaque normalized alpha only at that named boundary. A reference-containing decimal-backed custom `INumber<T>` proves ordinary consumer support without assuming blittable storage.

```sh
dotnet run --project samples/Lumyte.Mathematics.Sample -c Release
```

Assertions fail the process if a public operation returns an unexpected result. Shape-specific operations remain available on their value types. Arithmetic and boundary policies are explicit in method names, including `ContainsPolygonClosed`, `ContainsHalfOpenEdges`, `TransformAffineFused`, `TransformAffineChecked`, and BCL single-precision helpers.

The sample also checks `CssColors.Red`, `CornflowerBlue`, `Transparent`, immutable keyword enumeration and named lookup. These are eight-bit sRGB codes: call `CssColors.Red.NormalizeUnorm<float>(255)` for unit components. Drawing2D consumers pass the resulting `Rgba<float>` directly to their canvas API. `ConvertChecked<float>()` only casts codes (255 stays 255f); `NormalizeUnorm<float>(255)` produces unit components (255 becomes 1f), without decoding sRGB. Common storage also accepts finite raw/HDR values; drawing and UI validate unit colors when accepting them. Contextual colors and CSS parsing are outside the catalog.
