# CPU correlated sparse strips

Internal entry point:

```csharp
SparseStripPlan2D plan = SparseStripBuilder2D.Create(
    content, targetInfo, maxWorkingBytes, cancellationToken);
```

(The same overload accepts one DrawingCommand2D.) Default limit is 256 MiB.

- Commands retains original immutable command/paint/clip/resource identity, in painter order.
- Strips uses target-local integer X/Y, one row, Length pixels, CommandOrdinal, and Evaluation.
- MaskOffset == -1 means all 16 shape samples. Other offsets address Length ushort entries in BoundaryMasks. Bit 4*sy+sx corresponds to (x+(sx+.5)/4,y+(sy+.5)/4).
- Strip pixels never overlap within a draw. Empty pixels are omitted; full interior runs never store masks. Neither paint nor clip is averaged into shape coverage.
- The GPU painter must preserve strip/draw order, evaluate paint and normalized clip at the identical bits, average the sixteen premultiplied contributions, and use fixed-function SrcOver once per draw/pixel.
- Image, CoverageMask and DistanceField evaluations are conservative destination rectangles: evaluate the original descriptor on GPU (including transformed destination membership). MeshCenter means evaluate original mesh coverage/interpolation at pixel center, not binary shape membership; then multiply correlated paint/clip samples. Samples with DrawPaintGraphCommand2D means target-wide paint evaluation.

Numeric policy: transformed path edges are binary64 nested ordered FMA, half-open Y crossings and strict signed FMA orientation, matching normalized CPU clips. Fill closes every contour implicitly and applies NonZero/EvenOdd across all contours. Strokes reuse existing 0.125-pixel flattening and cap/join/miter expansion, then union triangle membership. Rectangle edges are half-open on the physical grid; rotated rectangles use the same winding predicate. Circle/rounded rectangle containment is analytic binary64 inverse-affine evaluation. Singular transforms produce no coverage.

Preparation performs horizontal 16-pixel tile traversal and exact per-sample classification, followed by run compression. Known full axis-aligned rectangle rows and conservative descriptor rows skip sample expansion. It does not yet have spatial edge bins/backdrop propagation: bounded sample-edge admission can reject complex/large scenes even when their final output is small. This is not an optimized scanline renderer or a speed claim.

CPU ownership is admitted before allocation using checked counts, exact two-pass output counts, a single row scratch mask, and conservative bounded flatten/stroke scratch reservation (32 MiB per active shape). Geometry is limited to 65536 path edges/stroke triangles, aggregate 262144 admission cells, target extents <=32768, and 268435456 conservative sample-edge work. Reservations can reject small paths under very low byte budgets. No dash model is introduced. Unsupported custom commands throw NotSupportedException; there is no backend fallback, truncation, or AA degradation.

WorkingBytes reports conservative peak CPU ownership only. The renderer must charge paint, normalized clips, uploads, GPU buffers and binding tables additionally against the same Prepare/Append MaxWorkingBytes; this module does not claim to enforce a whole-renderer GPU budget. The plan owns CPU arrays only and borrows original immutable command/resource inputs. It is not a rendered image or a GPU lifetime owner.
