# SceneV1 canonical ABI (version 1)

SceneV1 has exactly one unmanaged Vector4 stream, 16-byte stride, float32 coordinates. Integers in records are **uint32 bitcasts**, not numeric float conversions. Header, ordinal and offset cells may therefore contain nonfinite float bit patterns; do not run float finiteness checks over structural cells. Float-valued instruction and geometry cells are validated before packing. All offsets below are absolute Vector4 indices except instruction-local child/payload indices noted below.

Cell 0 uint4: magic 0x314E4353 (SCN1), logical-command count, sample count 16, grid version 1.

Each logical command, including empty clip, singular transform, zero-area and invisible commands, has exactly one expanded command record. The first starts at cell 1; follow header.next to the next. Logical ordinal and expanded ordinal are both stable zero-based contiguous values; triangle expansion does not reorder or create painter-order commands.

Six header cells per command:

0. uint4(logicalOrdinal, expandedOrdinal, next, completeEvaluationRoot).
1. uint4(instructionOffset, instructionCount, dataOffset, dataCount).
2. uint4(clipKind, outerClipMaskOffset, outerClipMaskCellCount, meshCenterCoverageFlag).
3. float4(scissorX, scissorY, scissorWidth, scissorHeight), physical pixels.
4. float4(analyticSampleLeft, Top, Right, Bottom), quarter-pixel integer sample-index bounds, right/bottom exclusive.
5. uint4(shapeRoot, paintRoot, commandKind, reserved=0). shapeRoot=UINT_MAX denotes a transparent fallback, never an instruction index. completeEvaluationRoot includes outer clip; independent passes use shapeRoot, paintRoot and the explicit outer clip instead of applying it twice.

Each instruction is eight Vector4 cells: T0,T1,A,B,C,D followed by uint4(kind,first,count,mode), then uint4(viewId,samplerId,0,0). Missing resources are UINT_MAX. Resource IDs index the separately captured RenderResourcesSnapshot2D.Views/Samplers lists. No IGpuRef, request, object pointer, texture or device identity is serialized. Device ResourceViews must bind this exact snapshot under the same source/generation owner lease; IDs must never be interpreted as native addresses or argument-table slots.

T0=(inverse.M11,inverse.M21,inverse.M31,0), T1=(inverse.M12,inverse.M22,inverse.M32,0); dot with (pixelX,pixelY,1) returns local position. Geometry remains in logical space, with transformed pixel-space tolerance driving CPU curve subdivision. Triangle expansion includes stroke cap/join/miter rules before packing.

Instruction opcodes preserve the established DrawingProgram2D numerical representation:

- 0 constant linear premultiplied RGBA A.
- 1 rectangle A=(x,y,width,height); 2 rounded rectangle with radius B.x; 3 circle A=(cx,cy,radius,0).
- 4 flattened closed fill edges: first/count index command-local data float4(ax,ay,bx,by), mode FillRule2D.
- 5 triangle shape: first indexes data, count triangles (three float4(position.xy,coverage,0) per triangle). Mode 0 binary stroke triangles, mode 1 nonbinary mesh: inverse-transform **pixel center**, barycentric interpolate, saturate, max overlapping triangles, broadcast the result to all sixteen samples. Never reinterpolate at sample offsets.
- 6/7/8 linear/radial/sweep gradient; first indexes pairs of data cells (offset cell then linear premultiplied color); count stops, mode ExtendMode2D. Parameters A/B use the existing program/shader conventions.
- 9 image; A source rectangle, B texture extent and tile modes, C filter/alpha encoding.
- 10 opacity(child=first,A.x); 11 multiply(first,count instruction children); 12 composite(first,count children,mode CompositeMode2D).
- 13 coverage mask / 14 distance field: A destination rectangle, B source rectangle, C texture extent/range, mode distance encoding.
- 15 distance-field paint: first child instruction; image parameters A/B/C and D=(distanceRange,mode,0,0).
- 16 CPU-normalized analytic clip wrapping first child, A sample bounds.
- 17 CPU-normalized mask wrapping first child; count is command-local data offset to packed mask; A physical pixel bounds.

first/count mean child instruction indices for opcodes 10,11,12,15,16,17 as described, and **command-local data** indices for opcodes 4,5,6,7,8; add header.dataOffset when accessing data. Instruction child indices add header.instructionOffset and multiply by 8. No GPU path flattening, stroke expansion or clip geometric predicate recomputation is permitted.

Outer clipKind: 0 None, 1 Empty, 2 integer Scissor, 3 Analytic, 4 SampleMask. For SampleMask each packed Vector4 stores four numerically represented uint16 masks, row-major within scissor bounds, padding zero; each bit s=4*sy+sx corresponds to offset ((sx+.5)/4,(sy+.5)/4). Empty contributes zero, None one, Scissor uses the integer bounds, Analytic uses sample-index bounds. The CPU FMA clip normalization and its existing sample-edge/mask limits are authoritative, including nested paint clips.

Host extent immutable scalar metadata: source Guid, width/height uint, samples=16 int, gridVersion=1 int, commandCount int. Scene and Frame metadata contain no managed/native resource references. Commands has host-only immutable commandsSnapshot; ResourceManifest has host-only immutable resourceSnapshot and two uint elements (view count,sampler count). Upload only serializes canonical element bytes, never extent metadata. These metadata snapshots are permitted solely by Core IImmutableSchemaMetadata.

FrameV1 cells: float4(width,height,pixelScale,1), float4(pixelOriginX,pixelOriginY,16,1). Coverage16V1 is float32, Clip16V1 float32 and Paint16V1 float4 linear-premultiplied, command-major then pixel row-major then sample s. Coverage validation requires exactly commandCount*width*height*16 entries. Compose averages coverage*clip*paint **per command**, then SrcOver in ordinal order, not per-sample compositing across commands.

Admission is checked before geometry expansion using a nonallocating count sink with the exact flattening primitive/tolerance/depth and conservative stroke triangle bound. Per-command full-target 16x16 tile references are a conservative upper bound. In addition the existing compiler's 256 instruction/262144-vector and clip normalization budgets remain hard limits; exceeding any bound rejects without quality degradation or fallback. MaxStrokeTriangles conservatively charges source mesh triangles as well as strokes.

Integration gaps: Device ResourceViews/Target pure projections and generation-bearing binding are not manufactured by these CPU files. Their renderer/Graphics host implementation must retain source owners and validate device generations. Schema descriptors for these endpoints declare identity layouts only; actual native texture/view usage and binding ABI remain the device binding's responsibility. No GPU algorithm or fake GPU processor is included.

The current Normalize factory declares a deliberately conservative finite host peak: `128 + 128 * max(1, MaxTileReferences) * (6 + 256*8 + 262144 + 262144)` bytes. The factor eight over wire bytes covers compiler/list growth, materialized scene and publication copies. This is correct but very pessimistic for large tile ceilings: the ADR example limits will not fit a 64 MiB Core host budget. A future caller-supplied explicit canonical-scene/paint-byte ceiling (or a build-time source command bound) is needed to make that example economical; do not silently reduce the declared bound. The graphics scratch policy is separate from this Core CPU bound.

For DrawCoverageMask and DrawDistanceField, the independent resource-free Coverage pass evaluates the destination rectangle of shape opcode 13/14 without sampling. Its paintRoot is the existing final multiply of sampled mask/distance coverage and paint. Paint therefore owns all image sampling and retains per-sample correlation; the completeEvaluationRoot used by fused stages is unchanged. This factorization adds no instruction and does not lower the existing 256-instruction acceptance limit.
