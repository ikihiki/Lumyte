# COLR v1 font fixtures

`Fonts/ColorFontTest.ttf` is the unmodified `test_glyphs-glyf_colr_1.ttf` from
[googlefonts/color-fonts](https://github.com/googlefonts/color-fonts/blob/main/fonts/test_glyphs-glyf_colr_1.ttf).
The 21 KB font is licensed under Apache-2.0; its license is included in
[Fonts/ColorFontTest-LICENSE.txt](Fonts/ColorFontTest-LICENSE.txt).

The fixture contains real COLR v1 solid, linear/radial/sweep gradient, affine
transform, nested glyph clip, repeated glyph reference and composite paint graphs.
Its deliberately cyclic `PaintColrGlyph` pair is mapped to U+F1100 and U+F1101.
Other examples include linear U+F0500, radial U+F0503, sweep U+F0200,
source-over U+F0A03, multiply U+F0A17 and a valid repeated reference U+F1200.

`Fonts/ColorFontScriptMetrics.ttf` is a derivative of that Apache-2.0 fixture,
subset to U+F0500 and renamed to Lumyte Color Script Test. Its OS/2 subscript
X/Y sizes are 500/600, superscript sizes 550/650, and Y offsets 200/300 in
1000 units per em. These nonzero metrics intentionally differ from DejaVu Sans
to verify that fallback color glyphs use their actual face's script metrics.

`ColorFontRenderingTests` exercises actual Skia color-font pixels, all 28 COLR
composite operators, foreground alpha, bitmap placement and lifetime, registered
v0/v1 fallback, and malformed table metadata. It uses the Linux native asset
package explicitly; it does not require installed system fonts.

`DistanceFieldGenerationTests` checks independent colored edge channels, true
distance alpha, contour holes and winding, sharp corners, quadratic/cubic curves,
asymmetric Y-down placement and allocation limits without a GPU.
