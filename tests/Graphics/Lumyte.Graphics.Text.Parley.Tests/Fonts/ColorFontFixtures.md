# COLR v1 font fixtures

`ColorFontTest.ttf` is the unmodified `test_glyphs-glyf_colr_1.ttf` from
[googlefonts/color-fonts](https://github.com/googlefonts/color-fonts/blob/main/fonts/test_glyphs-glyf_colr_1.ttf).
The 21 KB font is licensed under Apache-2.0; its license is included in
[ColorFontTest-LICENSE.txt](ColorFontTest-LICENSE.txt).

The fixture contains real COLR v1 solid, linear/radial/sweep gradient, affine
transform, nested glyph clip, repeated glyph reference and composite paint graphs.
Its deliberately cyclic `PaintColrGlyph` pair is mapped to U+F1100 and U+F1101.
Other examples include linear U+F0500, radial U+F0503, sweep U+F0200,
source-over U+F0A03, multiply U+F0A17 and a valid repeated reference U+F1200.

`ParleyColorPaintTests` and `ParleyColorConformanceTests` inspect analytic vector
paint graphs and all 28 COLR composite operators through the same native shaping
and paint extraction boundary used by applications. They check currentColor,
translucent foreground gradients, font paint alpha, outline placement, snapshot
lifetime and malformed paint graphs on every platform. Browser verification also
checks actual GPU pixels without a platform-specific font rasterizer.
The tests do not require installed system fonts.
