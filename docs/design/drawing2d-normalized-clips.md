# Drawing2D clip normalization contract

Baseline: `c2b941cc767f06cbfbd92b77e6cef2a9fad5a221`.

## Before

`DrawingProgram2D.ApplyClip` emits one Path and Multiply per captured clip. `drawing-fragment.slang` evaluates every node at each of the sixteen correlated sample positions. `PreparedRectangleBatch2D.Fill` uploads each rectangle clip; `rectangles-fragment.slang` loops over up to eight clips per sample. `DrawingBounds2D` supplies conservative bounds, not a containment proof.

## Required after-state

Preparation classifies each logical scope as Empty, None, Scissor, Analytic, or SampleMask. Empty suppresses allocation/draw where known. None and integer axis-aligned Scissor emit zero clip coverage instructions. A fractional axis-aligned intersection emits one analytic coverage instruction, not one per original rectangle. General intersections use one precomposed sixteen-bit sample mask per pixel, preserving holes, winding and captured transforms. GPU never loops over original clips. Paint child clips remain distinct scopes across composition/opacity and are not promoted globally.

AA invariant: for sample positions s=(x+(i+0.5)/4,y+(j+0.5)/4), output is sum(shape(s)*paint(s)*clipIntersection(s))/16. A mask bit j*4+i records the binary intersection at s, not averaged coverage. Averaged alpha multiplied after integration is invalid, including for gradients. Integer scissor removal is valid only when all surviving samples satisfy the original predicate. CPU/GPU transform dot products, cross products and FMA can disagree at boundaries; exact equivalence requires independent boundary tests before adopting CPU rasterization.

## Implementation constraints

Reuse PathFlattening2D and shader-data geometry ownership rather than add a geometry library. The existing Skia clip architecture is useful precedent (geometry/scissor, analytic, then mask) but does not establish matching sample semantics. CPU normalization must retain inverse-space comparisons when algebraic rectangle intersection changes float rounding. No AABB-inside-path elision is allowed. Invalid inverses retain transparent fallback. Captured transforms, target origin, DPR, extent and fill rules participate in identity.

Masks must be bounded by tight conservative scissor and finite work/edge budgets; storage can use packed sample bits, never an averaged alpha. Existing prepared-owner lifetimes must cover uploads, indirect references, GPU completion and command disposal; no mutable global mask cache. Graph producer/read declarations and shared-table ownership must remain valid. Rectangle instancing must retain its trivial path and cannot fall back wholesale to the interpreter.

## Clarification accepted 2026-11-12

CPU-generated clips use the CPU result as authoritative, rather than attempting
bit-identical reproduction of unspecified historical GPU float contraction.
Selection is deterministic from immutable clip geometry, captured transforms,
fill rules, target extent, pixel origin and DPR. Cache hit/miss, memory pressure,
frame timing and backend performance never select another execution path.
Budget exhaustion fails preparation; it never falls back to original GPU clips.

The implementation flattens using the existing pixel-error contract, forwards
vertices to device space with explicitly ordered binary64 FMA, and evaluates
binary64 winding on the existing sixteen sample positions. Canonical axis-aligned
rectangles are intersected on that same sample lattice on CPU; the GPU compares
one integer sample-index interval. Complex clips store sixteen membership bits
as exactly represented integer-valued float lanes (four pixels per float4), loaded
numerically without sampling/filtering. GPU shape and paint arithmetic is unchanged.
Boundary-only differences from the old inverse-space float clip predicates are
intentional under the CPU-authoritative clarification, not tolerance relaxations.

Paint child scopes never install a global scissor. An integer child clip requiring
coverage inside a composition is represented analytically at that child scope;
outer integer scissors install the hardware scissor and emit no residual node.
This scope distinction is deterministic, not adaptive CPU/GPU selection.

The original inverse-predicate preservation and historical bit-equivalence gate
above is superseded by this clarification. Exact CPU-oracle-to-GPU sample-bit
transmission and independent geometry/paint correlation tests remain required.

## Qualification gate

Design review must resolve CPU/GPU boundary equivalence and bounded representation before implementation. Then exact CPU/hardware/software RGBA, scope/lifetime/Graph tests precede a frozen BenchmarkDotNet whole-completed-frame A/B. No quality reduction, changed assertions, favorable pilot substitution or non-BDN headline measurement is acceptable.
