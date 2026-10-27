# Parley text integration tests

The tests load real bundled font bytes through the generated csbindgen boundary,
shape complete paragraphs with Parley and inspect the immutable 2D snapshots.
They have no SixLabors or Skia package dependency.

Coverage includes Latin and Arabic ligatures, mixed Hebrew/Latin visual order,
UTF-8-to-UTF-16 cluster mapping, Japanese and Khmer fallback, paragraph metrics,
wrapping and alignment, native-owner disposal, vector canvas recording and the
MTSDF counter of an outlined letter O.
Decoration regressions also require an overline-only style on B in ABC to
produce exactly B's line, and an explicit B override to remove an inherited
overline while preserving separate lines above A and C. These ranges inherit
the same paint and font settings so an unrelated style change cannot hide a
missing overline run boundary.
Tabs retain their source cluster and positive advance while contributing no
missing-glyph box. The test also compares the four visible outlines around a
tab against the same text without the tab, independent of paragraph translation.

The COLR cases require analytic vector output for all 32 paint formats and all
28 composite operations. Current-color samples independently evaluate
the recorded graph at an interior and exterior point to detect missing contours,
incorrect foreground transforms and duplicated alpha multiplication.

Most fonts are linked from [the shared text test fixtures](../Lumyte.Graphics.Text.Tests/README.md),
including their license files. Their COLR provenance is recorded in
[ColrVectorFixtures.md](../Lumyte.Graphics.Text.Tests/ColrVectorFixtures.md).

`Fonts/ParleyVariablePaint.ttf` is a 2,220-byte derivative of
`ColrVectorDefaultVariation.ttf` under the same Apache-2.0 license. Its family is
renamed to `Lumyte Parley Variable Paint`. The only change to the COLR variation
data is setting its single region to `(0,1,1)` instead of `(0,0,0)`. At the default
`TEST=0` instance, U+E00F retains the base translation `(30,40)`. At `TEST=1`, the
reverse delta-index mapping changes it to `(-70,240)`. At an em size of 64, that
must move the recorded paint bounds by `(-6.4,-12.8)` in Y-down layout coordinates.
This checks actual nondefault variation evaluation rather than accepting a
variable record whose default value happens to match a static record.

`Fonts/ParleyVariableOutline.ttf` is a 1,292-byte synthetic TrueType variable font
created for these tests with FontTools FontBuilder and varLib. It contains an
empty .notdef and an A represented by a rectangle, with no third-party outline
data. Its `wght` axis spans 100 through 900. The rectangle widens from 400 to 800
design units while the advance grows from 500 to 900, with a 1,000-unit em.
The test requires both shaping advances and extracted contours to follow the
same selected instance, exercising HVAR and gvar rather than a paint-only axis.
