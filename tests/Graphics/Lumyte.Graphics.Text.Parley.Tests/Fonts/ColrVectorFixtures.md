# COLR vector graph fixtures

`ColrVectorFormats.ttf` is a 2,728-byte derivative of the Google Fonts
Apache-2.0 [COLR test font](ColorFontFixtures.md). It retains the original
`upem_box_glyph` outline and three CPAL palettes, replaces COLR with small graphs
covering every paint format from 1 through 32, and renames its family to
`Lumyte COLR Paint Formats`. The original [license](ColorFontTest-LICENSE.txt)
applies to this derivative.

The private-use mappings U+E001 through U+E020 select formats 1 through 32.
Their glyph identifiers are format number plus one. Solid and gradient formats
are wrapped in `PaintGlyph` to provide finite drawing bounds. Variable formats
use `VarIndexBase = 0xFFFFFFFF`, including variable color stops and affine data,
to exercise parsing of the default instance without supplying a variation store.
The final composite glyph also has a ClipList box `(100,100,900,900)` in a
1,000-unit em.

`ColrVectorDefaultVariation.ttf` is a 2,224-byte derivative of the format
fixture, renamed to `Lumyte COLR Default Variation`. It retains a static
translation and its variable counterpart, one `TEST` variation axis with a
`(0,0,0)` active default region, delta rows `200` and `-100`, and a reverse
DeltaSetIndexMap. The resulting default translation must be `(-70,240)` rather
than the stored variable base `(30,40)`. This exercises actual ItemVariationStore
and index-map resolution, beyond simply ignoring all variation deltas.

`ColrVectorNoVariation.ttf` derives from the same variation fixture but maps
both indices to the valid `0xFFFFFFFF` no-variation sentinel. The paint must retain
its stored translation `(30,40)` even though the active variation store still exists.

`ColrVectorForegroundTransforms.ttf` is a 2,080-byte derivative with one
currentColor glyph under a translation and nonuniform scale. The glyph clip must
transform, while an external foreground gradient retains paragraph coordinates.

`ParleyColorPaintTests` and `ParleyColorConformanceTests` check this format coverage, static/default-variable
equivalence, CPAL selection, currentColor gradients and alpha, backend composite
operators, ClipList bounds, vector snapshot lifetime, direct canvas recording,
cyclic references and invalid paint tags. It inspects the public vector graph;
it does not accept a raster image as proof of vector support.

The original fixture's glyph 154 (U+F0B06) uses `PaintGlyph` to reference its own
color base outline. Its raw glyf square spans `(0,0)` to `(1000,1000)`, while its
hmtx left side bearing is 100 and ClipList bounds are `(100,250,900,950)`.
The regression checks both real contour edges and positioned bounds, then
independently evaluates the line clips at the center to require premultiplied
RGBA `(0.5,0,0,0.5)` for a half-transparent red foreground. This detects native
color-glyph path suppression and hmtx bearing normalization errors that graph
node presence alone cannot detect.

`ColrVectorCompositeMetrics.ttf` is a 1,192-byte subset of the original font,
renamed to `Lumyte COLR Composite Metrics`. U+F0B06 (glyph 1) is a TrueType
composite of the square translated by 200 design units, with USE_MY_METRICS
enabled; its hmtx bearing is also 200. Its ClipList allows the full composite.
The regression requires the translated outline to begin at logical X=6 for an
em size of 30, allowing 0.005 units for native curve-coordinate quantization.
