# Public mathematics consumer

An ordinary console project references Mathematics without friend-assembly access. It exercises the public `Geometry2D` / `Geometry3D` entry points, generic integer/decimal/BigInteger geometry, float BCL versus double FMA arithmetic, streaming struct sinks, and SIMD span operations. It also exercises `Rgba<T>` code normalization, packed pixel storage, sRGB transfer, alpha association, HSL/HSV, and `Color` batch conversion.

```sh
dotnet run --project samples/Lumyte.Mathematics.Sample -c Release
```

Assertions fail the process if a public operation returns an unexpected result. Shape-specific operations remain available on their value types. Arithmetic and boundary policies are explicit in method names, including `ContainsPolygonClosed`, `ContainsHalfOpenEdges`, `TransformAffineFused`, `TransformAffineChecked`, and BCL single-precision helpers.
