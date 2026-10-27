# ADR-GRAPHICS-0019: WASMで共有する文字組み・カラーフォント・距離場描画

- 状態: 採用
- 日付: 2026-10-26
- 更新: 2026-10-27

## 背景

既存の2Dシステムに、複数フォント、複雑な文字体系、双方向の文章、折り返し、装飾を扱う文字描画を追加する。ブラウザーのWASMでも同じ文字組みを使い、既存の変換・clip・paint・GPU資源の寿命契約へ接続する。拡大表示で角を保つMSDF/MTSDFと、COLR v1のpaintを扱うカラーフォントも必要となる。

[GRAPHICS-0018](GRAPHICS-0018-2d-graphics-engine.md)で分離した文字組みを独立パッケージへ置く。ライブラリの旧版へ長期間留まる構成と、外部のフォント・レイアウト型が利用側APIへ広がる構成は、文字組みの修正やエンジン交換を難しくする。Parleyを試験導入し、既存の描画を維持したまま保守可能な構成を評価する。

Rustと.NET WASMを接続するビルドでも、常設のPythonや特定のshellを前提にせず、.NET SDKを入口にする。toolchainの解決・準備とnative / WASMの段階を管理し、C#プロジェクトのbuildやpublishから同じ処理を呼べるようにする。

## 決定

### 適用範囲と依存方向

`Lumyte.Graphics.Text`を文字組みエンジンから独立した描画用snapshotと距離場のパッケージにする。新しい`Lumyte.Graphics.Text.Parley`でParleyとSkrifaを使い、RustのC ABIをcsbindgenで生成したC# bindingから呼び出す。これを今回の試作対象とする。全プラットフォームでの製品採用や性能比較の完了を意味しない。

```text
アプリが取得したフォントのStream
        ↓ ParleyTextEngine2D.AddFont
Lumyte.Graphics.Text.Parley
        ↓ csbindgenで生成したC# binding / C ABI
Rust: Parley → Fontique・HarfRust・ICU4Xで文字組み
        └ Skrifaで輪郭・COLR paintを取得
        ↓ 配置済みglyph・path segment・paint eventの配列
Lumyte.Graphics.Textの不変なTextLayout2D
        ├ 直接描画 → FillPath / FillRect
        └ DistanceFieldText2D → 通常輪郭のMSDF/MTSDF atlas + COLR vector paint
        ↓ 既存のpaint・clip・変換・composite・prepare/record
Renderer2D → Graphics.Abstractions → Vulkan / wgpu / Browser
```

Parleyは段落の配置・改行を担当し、Fontiqueは登録フォントの選択・fallback、HarfRustはshaping、ICU4XはUnicode解析、Skrifaはメトリクス・輪郭・COLR paintを担当する。C#側は受け取った輪郭を`Path2D`へ、カラーpaintを`PaintGraph2D`へ変換する。文字をbitmap化して独自2Dを置き換える構成にはしない。

比較用の旧実装は`Lumyte.Graphics.Text.SixLabors`へ分離する。SixLabors.Fonts 2.1.3とSkiaSharp 3.119.4への依存はこの任意パッケージだけに置き、Parleyや共通Textからは参照しない。従来のfactoryは`SixLaborsTextLayout2D.Create`へ移し、共通`TextLayout2D`から外部の`TextOptions`を取り除く。`TextRun2D`と`ColorFont2D`等もこの比較用アダプターに属する。

フォント取得、OSのフォント列挙、ResourceManager、DI、ファイルアクセスを必須経路にしない。Browserではアプリが配布フォントを非同期に取得し、同期のCPUレイアウト作成へ明示して供給する。OSのフォント一覧や暗黙のfallbackに依存しない。

### C ABI・生成・所有権

Rust shimは`native/lumyte_text_parley`に置く。依存はParley 0.12、Skrifa 0.44、build時のcsbindgen 1.9.8を基準とし、解決した依存グラフを`Cargo.lock`へ記録する。`--locked`で再現可能にしつつ、更新はlockの見直しと回帰検証を伴って行う。キー管理を避けるために旧版へ恒久固定する方針は採らない。

csbindgenはRustの`extern "C"`関数と`repr(C)`のデータ型から`NativeMethods.g.cs`を生成する。ParleyのRust型をC#へ直接公開せず、opaqueなengine/layout handleと整数・浮動小数・pointer/lengthの配列だけを境界にする。C#の`nuint`で`usize`を表し、32-bit WASMと64-bit nativeのpointer幅を維持する。グリフやsegmentごとにmanaged callbackを呼ぶ構成は避け、結果をまとめて読み出す。

engineは登録フォントと文字組みの作業領域をRust側で所有する。フォント入力は呼び出し中だけ借用し、必要なデータはRust側へコピーする。作成したnative layoutはglyph・path・paint・stop等の配列を所有し、viewのpointerはそのlayoutを解放するまでだけ有効とする。C#はこの間にデータをmanagedの不変modelへコピーし、Rustで確保したメモリをRustの解放関数で返す。生成した`TextLayout2D`とglyph情報はengineやnative layoutの解放後も使用できる。

C ABIにはversion取得とstatus／UTF-8エラー取得を設け、異常を例外としてC#へ伝える。RustのunwindをC ABI越しに通さない。WASMで`panic=abort`を使う場合は、予期しないpanicからの回復を保証する契約にはしない。引数の有限性・範囲、UTF-16からUTF-8への境界変換、出力配列の参照範囲を両側で確認する。

フォント、文字列、glyph数とsnapshotの出力には上限を設け、COLR paintと複合輪郭は循環・深さ・展開量を検証してから取得する。上限超過や未対応の入力はエラーとして返し、無制限な再帰や暗黙のbitmap化へ進めない。具体的な上限は[Parley README](../../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md#寿命スレッド失敗)で管理する。

nativeでは`cdylib`をロードする。Browserでは.NETのEmscripten toolchainとABIを合わせてRustの`staticlib`を作り、module名`liblumyte_text_parley`で静的P/Invoke tableから参照し、`dotnet.wasm`へリンクする。`wasm32-unknown-unknown`向けのコンパイル成功だけを、この.NET WASMとの接続検証と同一視しない。具体的なビルド手順は[Parley README](../../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md)に置く。

### .NETを入口にするビルド

Parleyのビルドを通常の.NET console projectへ置き、FalloutのtargetからRust toolchainの準備、Cargo、生成binding、成果物とnoticeのコピーを実行する。target graphと実行に必要な`Fallout.Build`を参照し、解決したNuGet依存をlock fileへ記録する。globalなFallout CLIは前提にしない。

MSBuildのnative / Browser targetsと手動実行は同じrunnerを呼ぶ。外部プロセスへは引数を配列として渡し、継承した環境にtoolchain固有の設定だけを追加する。C#とRustのABI、WASMの互換feature、標準ライブラリの再構築、静的module名はこの入口でも維持する。

利用できるRust toolchainがない場合は、リポジトリのビルド用領域へRustupと対象toolchainを準備する。Rustupのbootstrapは公式配布元のURLとSHA-256を固定して照合する。設定済みのRust環境も利用し、globalなPATHやshell profileを書き換えない。nativeのリンクに必要なOS側のcompiler / linkerと、.NETのwasm-tools workloadは前提条件として区別する。

Emscripten自体は内部でPythonを使う。明示指定、SDK同梱runtime、専用cacheの順で解決し、PATH上のPythonに依存しない。専用runtimeはURLとSHA-256を固定して.NETで取得・照合・展開し、同じinterpreterを.NET側とRust側のEmscriptenへ渡す。このPython解決は共通targetsへ置き、比較用SixLabors Browserサンプルの.NET側Emscriptenでも使用する。PythonのruntimeやFalloutを描画アプリのruntime依存へ追加しない。

初回のNuGet復元とtool取得にはネットワーク接続が必要となる。cache、上書き引数、対応OSと具体的な起動手順は[Parleyのビルド手順](../../../tools/text-parley/README.md)で管理する。この判断の対象はParleyのビルド入口とし、既存の共通開発環境セットアップ全体の置換は含めない。

### 不変なレイアウトと描画

`TextLayout2D.FromDrawings`は配置済みの描画列・bounds・advance・行数を受け取り、描画列をコピーする。入力はnullを含まず、boundsの各成分はfinite、幅・高さと行数は非負を要求する。通常輪郭を`TextGeometry2D`、カラーpaintを`TextPaintGeometry2D`で表し、`Drawings`でpainter orderを維持する。`Geometry`は距離場生成等に使う通常輪郭だけを列挙する。

`ParleyTextEngine2D.CreateLayout`は同期で文字組みし、フォントや変更可能な設定に依存しない描画snapshotを返す。`CreateDetailedLayout`はそれに加えて、glyph ID・font ID・配置・advanceと元文字列のcluster範囲を返す。glyph IDは文字コードではなく、そのface内の識別子である。元のUTF-8 byte範囲とC#のUTF-16範囲を分けて公開し、結合文字や合字を一文字ずつの描画へ分解しない。

font・size・weight・italic・Underline/Strikethrough/Overlineをlayoutまたはrunから指定し、Localeは言語別のglyph選択と文字解析へ渡す。

スタイルの範囲はUTF-16のStartを含みEndを含まない範囲にする。surrogate pairを途中で分ける境界は拒否する。書記素や合字の途中でstyleを分割する指定は上流のcluster処理に従い、指定したコード単位ごとのpaint・装飾の分割は保証しない。旧SixLaborsアダプターの書記素indexとは異なるため、移行時に範囲をそのまま流用しない。paintの座標は段落全体の論理座標を使う。engineの操作は内部で直列化し、呼び出し中は設定listや入力bufferを変更しないことを要求する。

`canvas.DrawText(layout, origin)`はSave/Translate/Restoreで配置し、呼び出し前後の変換とclipを維持する。レイアウト全体を同じ変換で動かし、gradient等のpaintをglyphごとに伸縮しない。snapshotはGPU資源を作らず、画像paintに含むtexture等の寿命は通常のDrawing2Dと同じく利用側が管理する。

### COLR v1と可変フォント

SkrifaのCOLR paint traversalからsolid、linear/radial/sweep gradient、transform、glyph clip、groupとcompositeを取り出し、C#側でDrawing2Dのpaint graphへ変換する。paletteの選択、currentColor、paint/stop alphaを保持し、輪郭とpaintに同じfont instanceのvariation座標を適用する。通常のカラーglyph描画にSkiaやカラーbitmapのuploadを必要としない。

palette index 0xFFFFはlayout/runのforeground paintを使う。単色に加えてgradient等の`PaintGraph2D`も使え、font側のalphaとforeground側のalphaをgraph上で掛け合わせる。glyph内部のpaintはpath・clip・groupの順序を保ち、最終的には有限の描画boundsへ配置する。任意のvariation軸はOpenType tagとdesign座標の組で指定し、フォントの既定値だけに限定しない。

SVG glyphやbitmap emojiを含む全カラーフォント形式の対応、Pango markupの互換性、縦組みの組版保証は今回の試作範囲に含めない。上流ライブラリの機能と、このアダプターで公開・検証した機能を区別する。公開した機能や制限の具体的な一覧は[Parley README](../../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md)で管理する。

### FillPathの接続

Drawing2DへFillPath、型付きrenderer、配送、既定実装、DI登録を追加する。既存の基本図形と同じpaint・clip評価を使い、NonZeroとEvenOddを扱う。開いたcontourは塗りの評価時に閉じる。

`DrawingCommandRenderers2D`は4番目の任意引数で`IFillPathRenderer2D`を受け取る。省略時はstatelessな既定実装を使い、既存の3引数の呼び出しを維持する。`CreateDefault`と`UseGraphics2D`にも同じ配送先を追加する。通常の輪郭描画は文字専用のtextureやpipelineを必要としない。距離場を選んだ場合だけ別のatlasを用意する。

### MSDF/MTSDFの生成と描画

距離場の生成にはMITライセンスのSeasonMSDF **0.2.0**を使う。同ライブラリのmanagedなmsdfgen系pipelineへ`Path2D`の線・二次曲線・三次曲線を渡し、edge coloringと距離の計算を行う。文字組みやフォント読込を別のエンジンへ切り替えず、整形済みの輪郭から生成する。単一のSDFをRGBへ複製する実装にはしない。

MSDFはRGBの異なるedge channelにpseudo-distanceを保持し、shaderではmedianで輪郭を復元する。MTSDFはRGBに加えalphaへtrue signed distanceを保持する。描画時の拡大ではRGB medianを使い、縮小時はalphaの距離へ移行してmultichannel特有の乱れを抑える。alphaを通常の透明度として解釈しない。距離場は線形の値で、0.5を輪郭、0.5より大きい値を内側とする。`DistanceRange`はsource texel単位の符号付き距離の総幅である。

Drawing2Dには`PaintGraph2D.DistanceField`を追加し、任意のchild paintを距離場のcoverageでmaskする。既存の矩形命令でglyphのplane boundsを描くため、専用のcommand rendererやDI登録を増やさない。Canvas clip・paint clipと同じsubpixel位置で評価する。pixel footprintは変換行列から求め、動的なpaint評価ループ内のderivativeに依存しない。

sourceは`Rgba8Unorm`または`Rgba16Float`のsampled viewとし、sRGB形式を拒否する。`ImagePattern2D.Source`は正の整数texel矩形、`PatternToLocal`はsource-relative texelから論理座標への可逆変換とする。factoryはraw channel、linear filter、透明な領域外へ設定を正規化する。bilinear tapをshaderで評価するため、専用のGPU samplerは不要である。atlasは通常の画像patternと同じく、Record前にSampledへ可視化し、GPU完了まで所有者を保持する。

CPU生成の結果は先頭行が上のRGBA8で、paddingを含む論理`PlaneBounds`を持つ。paddingは`ceil(DistanceRange / 2) + 1` texelとする。open contourは閉じ、空の輪郭は外側の値を持つ1×1画像にする。生成器の上限は入力4096 segment、出力4,194,304 pixel、設定した単辺寸法とし、上限超過はNotSupportedExceptionで拒否する。生成は同期処理で、独立した呼び出し間に変更可能な状態を共有しない。

`DistanceFieldText2D`は生成した通常輪郭ごとの画像をRgba8Unorm atlasへpackし、upload stagingを所有する。COLR paintはvector graphのまま保持し、輪郭とカラーの統合された描画順を保つ。カラーglyphのgradientやcompositeを距離場channelへ符号化しない。`CreateAsync`はCPU生成とGPU資源の準備までを担当し、`RecordUpload`は一度だけcopyとSampledへのbarrierを呼び出し元のcommand bufferへ記録する。submitと完了待ちは利用側に残す。`Draw`は距離場paintとCOLRのvector paintを、それぞれの配置矩形へ記録する。glyphごとのpaintを維持し、snapshot単位で繰り返し描画できる。フォント全体の永続cacheや自動退避は導入しない。upload staging・texture・viewのownerを、全uploadとdrawのGPU完了まで保持する。

### COLRのための2D paint拡張

`PaintGraph2D.Composite(source, backdrop, mode)`はCOLR v1の28種類のPorter-Duff・blend演算を扱う。sourceとbackdropはそのnodeの入力graphで、描画先attachmentの既存内容を暗黙に読むものではない。両者をshape・clipと同じsubpixel位置で評価し、glyph内部でisolatedに合成する。完成したglyphをtargetへ合成する通常のsource-overとは分離する。

COLR/CPAL仕様では、sRGBの色値にinverse transferを適用し、linear-lightのpremultiplied RGBAでstopを補間し、compositeとblendを計算する。これは既存Drawing2Dの色契約と一致するため、COLR用のsRGB作業空間を追加しない。非separableなHue/Saturation/Color/Luminosityも同じlinear RGBを対象にする。

gradientはCOLR tableの表現を保ち、退化した定義や空のColorLineを通常の入力として扱う。仕様上透明になる定義を、その場で例外に置き換えない。入力がNaNやInfinityなどmodel自体の有限性を破る場合の拒否は維持する。

| 入力 | 評価 |
| --- | --- |
| 空のColorLine | 透明 |
| 1 stop | 一定色 |
| 重複stop offset | 最初のstopの手前では最初の色、同じoffset以降では最後の色を使うhard edge |
| 2 stop以上でspanが0のRepeat/Reflect | 透明 |
| 3点linearで点が一致、または投影が退化 | 透明 |
| 同じ中心・半径の2円radial、または両半径0 | 透明 |
| radialの交点 | 補間半径が正の解だけを使い、有効な解の最大値を選ぶ |
| 同じ開始・終了角のsweep | Padはその角の前後で端色を切り替え、Repeat/Reflectは透明 |

完全形の2円radialは符号付きfinite半径を保持し、変数deltaが負の半径を作る場合も表現する。単一中心の簡略形radialは従来どおり正のradiusを要求する。sweepのsample角はY-downで時計回りの[0, 2π)を使い、開始・終了角は負数・範囲外・逆順も保持する。

paint modelのdepth 128・expanded node 65536と、標準rendererのpaint depth 64・命令256・geometry 262144個のVector4というbudgetを維持する。compositeが同じnodeを再利用する場合は、node参照と同じ変換の組み合わせをcompilerで共有し、同じpaintを不要に展開しない。巨大なgraphを無制限に受け付ける契約にはしない。

### ライセンスと対応環境

Parley・Fontique・Fontationsは主にMITまたはApache-2.0、HarfRustとcsbindgenはMIT、ICU4XはUnicode License系の条件に従う。`Cargo.lock`のregistry依存は[第三者ライセンス表示](../../../native/lumyte_text_parley/THIRD-PARTY-NOTICES.md)へ収録する。使用したRust toolchainの`COPYRIGHT-library.html`をbuild時に`RUST-STANDARD-LIBRARY-NOTICES.html`へコピーし、nativeとBrowserの出力・publishに同梱する。platform runtimeを別途配布する場合も、その表示を保持する。生成器のライセンスだけでruntime依存を説明しない。

比較用SixLaborsアダプターにはSplit Licenseの条件が残る。2.1.3の保持で利用条件がなくなるわけではなく、現行の公式security policyでは旧majorはセキュリティ修正の対象外である。新規構成の採用基準に旧版固定を置かない。現行版を選ぶ場合は、条件に応じたassembly-scoped community key等の運用とAPI移行を別途評価する。

| 環境 | 文字組み・輪郭取得 | 描画・配布 |
| --- | --- | --- |
| native | 同じRust shimのcdylibとcsbindgen binding | 対象OS/architectureのnative成果物と既存Graphics backend |
| Browser WASM | .NETのEmscriptenとABIを合わせたRust staticlib | dotnet.wasmへstatic linkし、既存Browser 2D backendへ接続 |

この表は構成を示すもので、全OS・全architectureの実行試験済み一覧ではない。publish・実行した環境と未検証の環境は、各READMEと検証結果で区別する。
現在のnative targetはhostのOS・architectureを対象にし、別RIDへのcross compileは未整備とする。

今回はソースツリーから参照する試作として提供する。各RIDのnative資産を収録する配布NuGetは未整備のため、Parleyと比較用SixLaborsのアダプターは`IsPackable=false`にする。配布対象のRID、成果物の取得方法、native資産の梱包は製品採用時に決定する。

### 公開API

比較元は本変更前の`9115746`。共通描画modelとParleyアダプターは、SixLabors・Skia・Rustの型を公開境界へ出さない。ベクトルと変換はSystem.Numericsの型を使う。主要なAPIを以下に示す。

```diff
+namespace Lumyte.Graphics.Text
+{
+    public sealed class TextLayout2D
+    {
+        // 描画列をコピーする。finiteな矩形・非負の寸法/行数とnullを含まない入力を要求する。
+        public static TextLayout2D FromDrawings(string text, Rect2D bounds,
+            Rect2D advanceBounds, int lineCount, IEnumerable<TextDrawing2D> drawings);
+        public string Text { get; }
+        public Rect2D Bounds { get; }
+        public Rect2D AdvanceBounds { get; }
+        public int LineCount { get; }
+        public IReadOnlyList<TextGeometry2D> Geometry { get; }
+        public IReadOnlyList<TextDrawing2D> Drawings { get; }
+    }
+    public abstract record TextDrawing2D;
+    public sealed record TextGeometry2D(Path2D Path, PaintGraph2D Paint) : TextDrawing2D;
+    public sealed record TextPaintGeometry2D(PaintGraph2D Paint, Rect2D Bounds) : TextDrawing2D;
+    public static class TextCanvas2DExtensions
+    {
+        // canvasの状態を保存し、配置済みの輪郭・paintを記録する。
+        public static void DrawText(this DrawingCanvas2D canvas,
+            TextLayout2D layout, Vector2 origin = default);
+    }
+    public static class DistanceFieldGenerator2D
+    {
+        public static DistanceFieldImage2D Generate(Path2D path,
+            float pixelsPerUnit = 1, float distanceRange = 8,
+            DistanceFieldMode2D mode = DistanceFieldMode2D.Mtsdf,
+            int maximumDimension = 2048);
+    }
+    public sealed class DistanceFieldImage2D
+    {
+        public int Width { get; }
+        public int Height { get; }
+        // paddingを含む論理矩形と、source texel単位の距離の総幅。
+        public Rect2D PlaneBounds { get; }
+        public float PixelsPerUnit { get; }
+        public float DistanceRange { get; }
+        public DistanceFieldMode2D Mode { get; }
+        // 先頭行が上のRGBA8線形距離。MTSDFのalphaはtrue signed distance。
+        public ReadOnlyMemory<byte> Pixels { get; }
+    }
+    public sealed class DistanceFieldText2D : IDisposable
+    {
+        // atlasとstagingを準備し、submitしない。生成中の取消は輪郭間で確認する。
+        public static ValueTask<DistanceFieldText2D> CreateAsync(IGraphicDevice device,
+            TextLayout2D layout, float pixelsPerUnit = 1, float distanceRange = 8,
+            DistanceFieldMode2D mode = DistanceFieldMode2D.Mtsdf,
+            int maximumAtlasDimension = 2048, CancellationToken cancellationToken = default);
+        public IGraphicsTextureView View { get; }
+        // 同じdeviceのRecording状態かつpass外へ一度だけ記録する。
+        public void RecordUpload(IGraphicsCommandBuffer commands);
+        public void Draw(DrawingCanvas2D canvas, Vector2 origin = default);
+        // 全upload/drawのGPU完了後に返す。完了待ちは行わない。
+        public void Dispose();
+    }
+}
+namespace Lumyte.Graphics.Text.Parley
+{
+    public sealed class ParleyTextEngine2D : IDisposable
+    {
+        public ParleyTextEngine2D();
+        // streamを所有せず、フォントデータを登録してfamily名を返す。
+        public string AddFont(Stream stream);
+        public string AddFont(ReadOnlySpan<byte> fontData);
+        // 同期でimmutableなCPU snapshotを生成する。paint省略時は黒。
+        public TextLayout2D CreateLayout(string text, ParleyTextOptions2D? options = null,
+            PaintGraph2D? paint = null);
+        public ParleyTextLayout2D CreateDetailedLayout(string text,
+            ParleyTextOptions2D? options = null, PaintGraph2D? paint = null);
+        // engineを返しても生成済みsnapshotへ影響しない。
+        public void Dispose();
+    }
+    public sealed record ParleyTextOptions2D
+    {
+        // nullなら最初に登録したfamily。fallbackも明示登録する。
+        public string? FontFamily { get; init; }
+        public IReadOnlyList<string> FallbackFontFamilies { get; init; }
+        public float FontSize { get; init; } // 既定16、正のfiniteな論理emサイズ。
+        public float WrappingLength { get; init; } // 既定+Infinity、正の幅。
+        public float LineHeight { get; init; } // 既定1.2、正のfiniteな倍率。
+        public ParleyTextAlignment2D Alignment { get; init; }
+        public ParleyTextDirection2D BaseDirection { get; init; }
+        public string? Locale { get; init; } // BCP 47 tag。
+        public ParleyTextDecorations2D Decorations { get; init; }
+        public int ColorPaletteIndex { get; init; }
+        public IReadOnlyList<ParleyFontFeature2D> FontFeatures { get; init; }
+        public IReadOnlyList<ParleyFontVariation2D> FontVariations { get; init; }
+        public IReadOnlyList<ParleyTextRun2D> TextRuns { get; init; }
+    }
+    public enum ParleyTextAlignment2D { Start, End, Center, Justify }
+    public enum ParleyTextDirection2D { Auto, LeftToRight, RightToLeft }
+    [Flags]
+    public enum ParleyTextDecorations2D { None = 0, Underline = 1, Strikethrough = 2, Overline = 4 }
+    public readonly record struct ParleyFontFeature2D(string Tag, uint Value = 1);
+    public readonly record struct ParleyFontVariation2D(string Tag, float Value);
+    public sealed record ParleyTextRun2D
+    {
+        // UTF-16の半開区間。surrogate pairを分割しない。
+        public int Start { get; init; }
+        public int End { get; init; }
+        public string? FontFamily { get; init; }
+        public float? FontSize { get; init; }
+        public float? FontWeight { get; init; } // 指定時は1～1000。
+        public bool? Italic { get; init; }
+        public ParleyTextDecorations2D? Decorations { get; init; }
+        public PaintGraph2D? Paint { get; init; }
+    }
+    public sealed class ParleyTextLayout2D
+    {
+        public TextLayout2D Layout { get; }
+        public IReadOnlyList<ParleyGlyph2D> Glyphs { get; }
+    }
+    public readonly record struct ParleyGlyph2D(uint GlyphId, uint FontId,
+        string FontFamily, int Utf8Start, int Utf8Length, int Utf16Start,
+        int Utf16Length, Vector2 Origin, float Advance, bool IsColor);
+}
+namespace Lumyte.Graphics.Drawing2D
+{
+    public enum CompositeMode2D
+    {
+        Clear, Source, Destination, SourceOver, DestinationOver,
+        SourceIn, DestinationIn, SourceOut, DestinationOut,
+        SourceAtop, DestinationAtop, Xor, Plus, Screen, Overlay,
+        Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight,
+        Difference, Exclusion, Multiply, Hue, Saturation, Color, Luminosity
+    }
+    public sealed record CompositePaintNode2D(PaintNode2D Source,
+        PaintNode2D Backdrop, CompositeMode2D Mode) : PaintNode2D;
+    public enum DistanceFieldMode2D { Msdf, Mtsdf }
+    public sealed record DistanceFieldPaintNode2D(ImagePattern2D Pattern,
+        float DistanceRange, DistanceFieldMode2D Mode, PaintNode2D Child) : PaintNode2D;
+    public sealed class PaintGraph2D
+    {
+        // source-first。入力はisolated graphで、target内容をbackdropとして読まない。
+        public static PaintGraph2D Composite(PaintGraph2D source, PaintGraph2D backdrop,
+            CompositeMode2D mode = CompositeMode2D.SourceOver);
+        // COLR定義を保持する完全形。空stopや退化定義はshaderで透明等へ評価する。
+        public static PaintGraph2D LinearGradient(Vector2 p0, Vector2 p1, Vector2 p2,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 完全形の半径は符号付きfinite値。簡略形のradiusは正数のまま。
+        public static PaintGraph2D RadialGradient(Vector2 c0, float r0, Vector2 c1, float r1,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 角度spanが0のPadはhard edge、Repeat/Reflectは透明へ評価する。
+        public static PaintGraph2D SweepGradient(Vector2 center, float startAngle, float endAngle,
+            ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend);
+        // 線形RGBAの距離場でchild paintをmaskする。rangeは正のfiniteなsource texel数。
+        // raw channel、linear filter、透明な領域外へ正規化し、sRGB storageを拒否する。
+        public static PaintGraph2D DistanceField(ImagePattern2D pattern,
+            float distanceRange, PaintGraph2D paint,
+            DistanceFieldMode2D mode = DistanceFieldMode2D.Msdf);
+    }
+    public abstract class DrawingCanvas2D
+    {
+        // 既存pathを不変な参照として記録。開いた輪郭は塗り時に閉じる。
+        public void FillPath(Path2D path, Color2D color,
+            FillRule2D rule = FillRule2D.NonZero);
+        public void FillPath(Path2D path, PaintGraph2D paint,
+            FillRule2D rule = FillRule2D.NonZero);
+    }
+    public sealed record FillPathCommand2D(Path2D Path, PaintGraph2D Paint,
+        FillRule2D Rule, DrawingState2D State) : DrawingCommand2D(State);
+    public interface IFillPathRenderer2D
+    {
+        ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
+            ReadOnlyMemory<FillPathCommand2D> commands,
+            CancellationToken cancellationToken = default);
+    }
+    public sealed class DefaultFillPathRenderer2D : IFillPathRenderer2D
+    {
+        public DefaultFillPathRenderer2D();
+    }
+}
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed class DrawingCommandRenderers2D
     {
-        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
-            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle);
+        // nullならstatelessな既定path rendererを生成する。
+        public DrawingCommandRenderers2D(IFillRectRenderer2D fillRect,
+            IFillRoundedRectRenderer2D fillRoundedRect, IFillCircleRenderer2D fillCircle,
+            IFillPathRenderer2D? fillPath = null);
+        public IFillPathRenderer2D FillPath { get; }
     }
 }
```

SixLabors比較用のAPI・既定variation限定のCOLR reader・明示的なbitmap oracleは、[任意アダプターのREADME](../../../src/Graphics/Lumyte.Graphics.Text.SixLabors/README.md)を参照する。共通TextやParleyの依存には含めない。

## 検討した代替案

| 候補 | 特徴 | 今回の判断 |
| --- | --- | --- |
| HarfBuzz draw/paint + ICU等 | 成熟したshaping、輪郭・COLR paint、公式WASM実績。FreeTypeを必須にしない構成も可能 | fallback・幅に応じた改行・行配置等の統合作業が大きいため、まず段落層を含むParleyを試す |
| Pangoそのもの | 多言語の段落レイアウトを直接使える | GLib等の依存のWASM buildとstatic配布、独自2Dへの接続が必要 |
| COSMIC Text | Rustの複数行レイアウト・fallback・編集機能 | 同系統の比較対象。今回はParleyの段落出力とSkrifaのpaint経路を先に評価する |
| SixLabors.Fonts現行版 | managedで文字組み・新しいglyph ID描画APIを使える。条件に合う公開プロジェクトには同梱可能なcommunity keyがある | 更新可能な代替案として残す。旧版固定の比較アダプターと、Parleyの依存は分離する |
| RichTextKit + SkiaSharp / HarfBuzzSharp | .NETで段落と公開glyph runを使える | native資産とWASM品質の検証が必要で、Skia描画との責務重複もある |
| LayoutFarm/Typography | managedでフォント解析・輪郭を取り出せる | upstreamのglyph layoutの安定性と必要scriptの品質を確認する作業が残る |
| 手書きのC# DllImport | 少数の関数なら実装できる | ABI宣言の二重管理を避け、Rustの宣言からcsbindgenで生成する |
| Rustの内部型を直接公開するFFI | bindingの型数を減らせる | ABIの安定性、寿命、Rust更新の影響を局所化するためC ABIへ限定する |
| SkiaでCOLR glyph全体をbitmap化 | 既成のpaint engineでrasterを作れる | 独自2DのCOLR対応を活かし、拡大時のベクター品質とpaintを保持する |
| native msdfgen | MSDF/MTSDFの参照実装を利用できる | 距離場生成は既存のmanaged SeasonMSDFを維持し、文字組みの交換と独立させる |

### ビルド入口

| 候補 | 特徴 | 今回の判断 |
| --- | --- | --- |
| Python script | ファイル操作と外部プロセスを短く記述できる | 常設のinterpreterを要求せず、.NETから起動するFallout runnerへ移す |
| MSBuild XMLへ全処理を記述 | C#プロジェクトのbuildと同じ入口を使える | tool取得・環境設定・失敗処理をC#で共有し、targetsは呼び出しとSDK値の受け渡しを担当する |
| 独自の.NET console runner | .NETだけを入口にできる | targetの依存関係とパラメーター処理にはFalloutを使い、Parley固有の処理へ集中する |

## 結果と影響

文字組みエンジンを変えても共通snapshot・DrawText・距離場atlas・既存の2D paintを維持できる。SixLaborsの型とライセンスをParley利用側の公開境界から外し、輪郭とカラーpaintも同じfont instanceで取得できる。C ABIと生成bindingによって、Rustの変更箇所とmanagedの利用箇所を区別する。

Rust toolchain、native成果物の各OS向け配布、WASMのABI調整が新しい保守対象になる。Parley・HarfRustのAPIと文字組み品質、入力fontの扱いを継続して検証する。C ABIの生成だけでメモリ安全性や所有権が自動的に保証されるわけではない。配列のcopy、解放、index変換とnative errorの扱いをこちらで実装する。

Parleyのビルド入口を.NETへ統一し、Falloutのglobal toolやシステムPythonの事前導入を不要にする。専用toolchainの取得元・checksum・cacheと、Fallout依存も継続して更新する対象になる。Emscripten内部のPython使用と、利用者がinterpreterを常設する必要があることは区別する。

Fontiqueのsynthesis提案に含まれる疑似太字・疑似斜体は適用せず、登録済みの実faceまたは可変軸によるstyle選択を対象にする。Pangoと同一のAPI、markup、全言語の禁則処理、縦組み、全フォント形式の完全互換は約束しない。段落や編集に関する上流機能を、このアダプターの公開・検証済み機能と同一視しない。Parley上流には改行位置に応じた合字・筆記体の再shaping等の未完了部分もあるため、複雑な強制改行までPangoと同等の品質とは扱わない。性能・配布サイズと、未検証のOS/architectureの製品対応は評価を継続する。

## 検証方針

実フォントを使い、合字、結合文字、Arabic・Indic、混在bidi、日本語fallback、折り返し・配置、スタイル範囲とclusterのindexを確認する。font・engine・native layoutの寿命とsnapshotの独立性、不正な入力、C ABIの型幅・status・解放を確認する。COLRはSkrifaから受け取るpaintと既存GPU graphを照合し、palette、currentColor、半透明、variationを確認する。

Drawing2Dの全compositeとgradient境界条件のCPU・GPU検証を維持する。MSDF/MTSDFは通常輪郭の穴・角・拡縮を確認する。WASMはpublish成功だけでなく、実際のブラウザー上でParleyの文字組み・輪郭・カラーpaintを既存2Dへ接続して描く。未実施の結果を検証済みとして記録しない。

### 試作で確認した結果

2026-10-27にLinux x64で、ParleyアダプターのC# 90ケース、Rust shimの13テストと警告をエラーにしたclippyが成功した。比較用SixLaborsアダプターへの分離後も既存91ケースが成功した。C#プロジェクトからのCargo実行、csbindgenの再生成、native成果物とライセンス表示のコピーも確認した。

.NET Browser WASMでは、同じEmscriptenに合わせたRust標準ライブラリとstaticlibをリンクしてpublishし、headless ChromiumのWebGPUで文字を描画した。合字・日本語fallback・bidi cluster・折り返し、MSDF/MTSDFの4倍拡大時の穴、COLR v1のgradientとmultiply、半透明・gradientのcurrentColorをGPU読み戻しで確認した。engine解放後にもsnapshotを描画でき、SixLabors・Skia runtimeへの依存はない。

ビルド入口をFalloutへ移した後も、Linux x64でMSBuildからNative targetを実行してCargo・csbindgen・成果物とnoticeのコピーを確認し、C#統合テスト90ケースが成功した。TestRust target経由の13テストと、ビルド入口の.NETプロセス統合テスト9件も成功した。専用Pythonは固定archiveの取得・SHA-256照合と起動を確認した。

さらにPATHからPythonとglobalなCargoを外してParley Browserサンプルをpublishし、専用Pythonを.NET側・Rust側のEmscriptenへ渡したビルドが成功した。その成果物もheadless Chromium / WebGPUで描画・GPU読み戻しの検証に成功した。ビルド入口に関する検証範囲は[ビルド手順](../../../tools/text-parley/README.md#確認した範囲)へ記録する。

比較用SixLabors Browserサンプルも、同じPythonのないPATHでEmscriptenのcompile・link・wasm-optを含むpublishが成功した。

nativeの実行確認はLinux x64に限り、Windows・macOS・他architectureでの実行、配布サイズ・性能の比較は未検証とする。

## 参考資料

- [Parley](https://github.com/linebender/parley)
- [Skrifa / Fontations](https://github.com/googlefonts/fontations/tree/main/skrifa)
- [HarfRust](https://github.com/harfbuzz/harfrust)
- [csbindgen](https://github.com/Cysharp/csbindgen)
- [Fallout](https://github.com/Fallout-build/Fallout)
- [python-build-standalone](https://github.com/astral-sh/python-build-standalone)
- [RustのEmscripten target](https://doc.rust-lang.org/rustc/platform-support/wasm32-unknown-emscripten.html)
- [OpenType COLR specification](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)
- [OpenType CPAL specification](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)
- [SeasonMSDF](https://github.com/season-studio/SeasonMSDF)
- [SixLaborsのsecurity policy](https://github.com/SixLabors/Fonts/blob/main/SECURITY.md)
- [SixLaborsの現行ライセンス運用](https://docs.sixlabors.com/articles/fonts/index.html)
