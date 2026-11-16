# Separate stroke qualification

A road center's distance to any geographic segment is not an opaque-pixel oracle.
Actual inputs include quantized tile geometry, width, endpoint cap, join/miter
limit, clip stack and ordered commands.

Each command evaluates shape, clip and paint on the correlated 4x4 grid (offsets
1/8, 3/8, 5/8, 7/8), averages that draw, then composites in painter order. Separate
draws do not union their sample masks before averaging. Two abutting half-covered
butt strokes yield alpha 3/4, not 1. A continuous path is different input. Changing
map topology, caps or style to conceal this would be a separate policy decision;
retain separate-feature semantics by default.

## Handmade regression

Drawing2DTests.SplitStroke.cs uses invented coordinates only. Width-two horizontal
strokes run from (0, 1.5) to (2.5, 1.5), then to (5, 1.5). At pixel (2, 1), butt
masks are 0x3333 and 0xcccc. Red then blue yields premultiplied linear RGBA
(1/4, 0, 1/2, 3/4). Square caps both cover the pixel, so the last blue draw wins.
Clipping at x=2.5 retains correlation: one butt draw disappears, the other
contributes 1/2. A continuous white path gives 1, or 1/2 with that clip. A
right-angle corner distinguishes miter from bevel (16 versus 10 samples), with a
nested clipped paint graph. Expectations are exact binary fractions in transparent
Rgba16Float, avoiding color encoding and tolerance ambiguity. Both the historical
constructor and standard factory execute on Vulkan and native wgpu.

The fractional-clip case exposed an independent standard-renderer resource bug:
membership pages required eight registered buffers, exceeding default WebGPU's
seven-buffer draw budget. Membership pages now concatenate same-typed paint/clip
directories into one stream and relocate outer/nested clip IDs. Combined buffer
ranges remain bounded. This changes no sampling, topology, painter order or shader
coverage predicates. The original hardware failure remains in local artifacts;
a deterministic seven-slot preparation test also guards it.

## Local source evidence

Ignored artifacts are under artifacts/road-contract. They are not published; the
original data worktree remains read-only. Full original tile scope and all 90 probes
remain intact. The reference independently decodes all MVT road commands, builds
butt bodies and limited miters, enumerates 16 samples, averages and composites per
draw. It preserves failed ray/double/unfused references. Boundary-sensitive
arithmetic is explicit: exact endpoint membership changes with permitted float
multiply-add contraction. The RADV-matching cross determinant rounds its first
product, then fuses the negative second product. It is not a universal cross-device
exact-boundary oracle.

At the disputed pixel, masks are 0x000c (2/16) and 0xfff3 (14/16). The second body
mask is 0x7ff3; its miter contributes 0x8000. White over black therefore gives
14/16 + (2/16)(1 - 14/16) = 0.890625 linear, which encodes to 242 sRGB. Transparent
GPU replays independently confirm 0.125, 0.875 and 0.890625 for each draw and their
pair, with both renderers. Original 255 expectations fail; model-derived local
expectations pass 90/90 for both cached full-tile images. This is an oracle error,
not a road rasterization fix.

## Reproduction

Enable LUMYTE_GRAPHICS_GPU_TESTS=1 and select the verified hardware ICD with
VK_DRIVER_FILES. Run dotnet test in Release for tests/Graphics/Lumyte.Graphics.Tests
with --filter SplitStroke, then the full Drawing2D and Maps test projects. Use the
repository's SDK, shader compiler and validated native dependencies. Local env.sh
and logs record the exact host commands. No benchmark results or OSM source
coordinates are committed here.
