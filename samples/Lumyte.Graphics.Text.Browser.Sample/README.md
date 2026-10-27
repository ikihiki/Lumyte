# WebAssembly 文字描画サンプル（SixLabors比較用）

任意の`Lumyte.Graphics.Text.SixLabors`アダプターを使う比較用サンプルです。
Parleyの試作は[Parley Browserサンプル](../Lumyte.Graphics.Text.Parley.Browser.Sample/README.md)を参照してください。

同梱の DejaVu Sans を stream から読み込み、SixLabors.Fonts で文字組みして、通常の Renderer2D で WebGPU に描画します。輪郭の MSDF/MTSDF 生成には managed の SeasonMSDF を使います。COLRv1 はフォントの paint graph を Drawing2D の gradient・clip・transform・composite へ変換し、通常の GPU shader で評価します。SkiaSharp は glyph のベクター輪郭取得に使い、カラーフォントの bitmap 化は行いません。`SkiaSharp.NativeAssets.WebAssembly` が輪郭取得に使う静的ライブラリを .NET の WASM runtime にリンクします。OS のフォントは使いません。

Latin・Arabic・Hebrew の混在、結合文字、日本語へのフォント fallback、折り返し、下線、run の色、グラデーション、変換・clip を表示します。追加の行では MSDF と MTSDF の低解像度 atlas を 4 倍に拡大し、COLRv1 の linear・radial・sweep gradient と multiply composite を直接ベクター描画します。

## 実行

リポジトリの.NET SDKを用意し、リポジトリルートから実行します。

```sh
dotnet workload install wasm-tools --version 10.0.401
dotnet publish samples/Lumyte.Graphics.Text.Browser.Sample -c Release
```

`EmscriptenPython.targets`が、[共通のFallout runner](../../tools/text-parley/README.md)から
Emscripten用のPythonを解決します。PATH上のPythonやFalloutのglobal toolは前提にしません。
この比較用サンプルでは、ParleyのRustライブラリはbuildしません。

公開された`wwwroot`をHTTPサーバーで配信し、WebGPU対応ブラウザーで開きます。正常時は描画結果と `WASM text rendering passed` が表示されます。文字は 2D の sRGB texture に描画し、GPU readback 後に canvas へ表示します。readback に文字の画素が存在し、vector・MSDF・MTSDF の `O` の内部に穴が残り、COLRv1 の各 glyph に異なる色が残ることも検証します。

下部のテスト用図形では COLRv1 の全 28 composite mode を、透明な領域を含む source/backdrop と独立した描画先の背景色で評価します。交差部・source のみ・backdrop のみ・両方透明の画素を、線形光・premultiplied alpha の独立した CPU 計算と照合します。dodge/burn の端点、灰色の非分離 blend、gradient の空 stop・重複 stop・ゼロ範囲・符号付き半径等も GPU readback で確認します。実フォントの currentColor glyph でも、半透明色と外部 gradient の画素を照合します。

Node.js 24 と Chromium がある環境では、publish 後に headless browser でも確認できます。

```sh
node samples/Lumyte.Graphics.Text.Browser.Sample/verify-browser.mjs
```

`wasm-tools` workload が必要なため、既存の Browser サンプルと同じく通常のソリューションビルドには含めません。フォントの配布条件は [Fonts/LICENSE.txt](Fonts/LICENSE.txt)、文字組みライブラリの利用条件は [Graphics.Text.SixLabors](../../src/Graphics/Lumyte.Graphics.Text.SixLabors/README.md) を参照してください。

距離場 atlas は `DistanceFieldText2D.CreateAsync` で準備し、同じ command buffer へ `RecordUpload` を呼んでから `PreparedDraw2D.Record` を記録します。その所有者は submit と GPU 完了の後まで維持します。COLRv1 のレイアウトは `canvas.DrawText(colorLayout)` で直接記録し、カラー glyph 用の texture upload は不要です。

## 同梱フォント

DejaVu Sans は SixLabors.Fonts v2.1.3 のテスト資源から取得した原本で、Bitstream Vera license と public domain の変更部分から成ります。[配布条件](Fonts/LICENSE.txt)を同梱します。

日本語は [Google Fonts の Noto Sans JP](https://github.com/google/fonts/tree/main/ofl/notosansjp) の `NotoSansJP[wght].ttf` を FontTools で weight 400 の static font に変換してから、このサンプルの「日本語の文字描画：折り返しと装飾」「こんにちは、世界！」に必要な文字だけへ subset し、family 名を `Lumyte Sample Japanese` へ変更したものです。[SIL Open Font License](Fonts/NotoSansJP-OFL.txt)を同梱します。この subset は任意の日本語入力を網羅しないため、実アプリでは必要なグリフを持つフォントを登録してください。

`ColorFontTest.ttf` は [googlefonts/color-fonts](https://github.com/googlefonts/color-fonts/blob/main/fonts/test_glyphs-glyf_colr_1.ttf) の原本で、[Apache-2.0 license](Fonts/ColorFontTest-LICENSE.txt)を同梱します。U+F0500（linear）、U+F0503（radial）、U+F0200（sweep）、U+F0A17（multiply）を使います。
