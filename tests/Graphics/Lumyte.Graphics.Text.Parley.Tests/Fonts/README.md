# Bundled font fixtures

These files are test inputs supplied explicitly to the production text engine.
They do not add a font library or require installed system fonts.

- `DejaVuSans.ttf` is an unmodified font obtained from the SixLabors.Fonts v2.1.3
  test assets. Its outlines are distributed under the Bitstream Vera font license
  with DejaVu changes in the public domain. Copyright and distribution conditions
  are preserved in [LICENSE.txt](LICENSE.txt).
- `NotoSansKhmer-Regular.ttf` was obtained from the same test assets and is under
  the SIL Open Font License 1.1. Its copyright notice and license are preserved in
  [NotoSansKhmer-LICENSE.txt](NotoSansKhmer-LICENSE.txt).
- `LumyteSampleJapanese.ttf` derives from Google Fonts' Noto Sans JP variable font.
  It was instantiated at weight 400, subset to the bundled sample's Japanese text
  and renamed to `Lumyte Sample Japanese`. Its SIL Open Font License 1.1 and
  copyright notice are preserved in [NotoSansJP-OFL.txt](NotoSansJP-OFL.txt).
- The Google COLR test font and its derivatives are documented in
  [ColorFontFixtures.md](ColorFontFixtures.md) and
  [ColrVectorFixtures.md](ColrVectorFixtures.md). They retain
  [the Apache-2.0 notice](ColorFontTest-LICENSE.txt).
- The two Parley variable-font fixtures are documented in the
  [test project README](../README.md).
