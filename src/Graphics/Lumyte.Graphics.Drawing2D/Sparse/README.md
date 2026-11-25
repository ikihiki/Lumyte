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

Numeric policy: transformed path edges are binary64 nested ordered FMA, half-open Y crossings and strict signed FMA orientation, matching normalized CPU clips. Fill closes every contour implicitly and applies NonZero/EvenOdd across all contours. Strokes reuse existing 0.125-pixel flattening and cap/join/miter expansion, then union triangle membership. Rectangle edges are half-open on the physical grid; rotated rectangles use the same winding predicate. Circle/rounded rectangle containment is analytic binary64 inverse-affine evaluation. Circle radius boundaries are strict; rounded rectangle corners remain inclusive. Shape intent is retained explicitly even when a rounded square and a circle have identical bounds and radius. Singular transforms produce no coverage.

Preparation performs horizontal 16-pixel tile traversal and exact per-sample classification, followed by run compression. Known full axis-aligned rectangle rows and conservative descriptor rows skip sample expansion. It does not yet have spatial edge bins/backdrop propagation: bounded sample-edge admission can reject complex/large scenes even when their final output is small. This is not an optimized scanline renderer or a speed claim.

CPU ownership is admitted before allocation using checked counts, exact two-pass output counts, a single row scratch mask, and conservative bounded flatten/stroke scratch reservation (32 MiB per active shape). Geometry is limited to 65536 path edges/stroke triangles, aggregate 262144 admission cells, target extents <=32768, and 268435456 conservative sample-edge work. Reservations can reject small paths under very low byte budgets. No dash model is introduced. Unsupported custom commands throw NotSupportedException; there is no backend fallback, truncation, or AA degradation.

WorkingBytes reports conservative peak CPU ownership only. The renderer must charge paint, normalized clips, uploads, GPU buffers and binding tables additionally against the same Prepare/Append MaxWorkingBytes; this module does not claim to enforce a whole-renderer GPU budget. The plan owns CPU arrays only and borrows original immutable command/resource inputs. It is not a rendered image or a GPU lifetime owner.

## Explicit generic-pipeline upload ownership

`CpuSparseCoverage2D` produces invocation-derived `Host<Coverage16V1>`, not reusable source data. Generic direct, retained, two-page paging and parent-Graph fixtures therefore connect its real row masks using `Transfers.Upload<Coverage16V1>(UploadRetention.Fresh)`, followed by the same Root paged Paint/Clip/Compose consumers as their GPU-coverage controls. The generic paging CPU-control coverage upload is Fresh too. No schema-name inference or replacement coverage producer is used.

The parent-Graph Download-to-reupload structural control also marks that intermediate upload Fresh. Original canonical Scene, Frame and resource projections retain their existing source eligibility; legacy source/frame/resource cache ownership is unchanged. The no-argument Upload constructor/helper remains Source-compatible. Fresh bypasses immutable-source admission and uses invocation-owned storage; this declaration is shared by runtime routing and captured writer preflight.

The prior 78 native plus 13 legacy passing cases predate this Source/Fresh integration and are not current-tree GPU qualification. Rebuild CPU gates independently; native image, retained ownership and Download readback qualification require a new serial GPU grant. No tolerance, sample count, painter ordering, all-command mask, bounds, transform or COW ownership oracle is relaxed by this migration.
