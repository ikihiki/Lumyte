# Lumyte.Graphics.Text.Tests

共通snapshot・距離場と、任意の`Lumyte.Graphics.Text.SixLabors`アダプターの比較テストです。
文字組みのfactoryは`SixLaborsTextLayout2D.Create`を使います。
Parleyは[専用テスト](../Lumyte.Graphics.Text.Parley.Tests)で検証します。

同梱したDejaVu SansとNoto Sans Khmerで、OpenType合字、アラビア文字の結合、双方向文字、
結合文字の書記素範囲、フォントフォールバック、折り返し、空白のadvance、装飾、
snapshotの不変性、Canvasの状態保存を検証します。
OSのフォントやGPUに依存しません。

```sh
dotnet test tests/Graphics/Lumyte.Graphics.Text.Tests
```

`Fonts/DejaVuSans.ttf`はSixLabors.Fonts v2.1.3のテスト資産から取得した
[DejaVu Sans](https://github.com/dejavu-fonts/dejavu-fonts)です。
再配布条件と著作権表示は[Fonts/LICENSE.txt](Fonts/LICENSE.txt)に同梱しています。

`Fonts/NotoSansKhmer-Regular.ttf`も同じテスト資産から取得しています。
フォントに記録されたGoogleの著作権表示とSIL Open Font License 1.1を
[Fonts/NotoSansKhmer-LICENSE.txt](Fonts/NotoSansKhmer-LICENSE.txt)に同梱しています。

`Fonts/LumyteSampleJapanese.ttf`は[Google FontsのNoto Sans JP](https://github.com/google/fonts/tree/main/ofl/notosansjp)を
fontTools 4.61.1でweight 400へ固定し、サンプルで使う
「こんにちは、世界！日本語の文字描画：折り返しと装飾」のglyphへ縮小したものです。
変更版のfamily名はLumyte Sample Japaneseに変更しています。
[Fonts/NotoSansJP-OFL.txt](Fonts/NotoSansJP-OFL.txt)に原著作権表示とOFL 1.1を同梱しています。
