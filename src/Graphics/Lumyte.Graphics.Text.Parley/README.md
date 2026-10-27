# Lumyte.Graphics.Text.Parley

Parleyで段落を組み、Skrifaから輪郭とCOLR paintを取り出して、Lumyteの既存2D描画へ渡す試作アダプターです。
C#とRustのC ABIは[csbindgen](https://github.com/Cysharp/csbindgen)で生成します。
この構成はSixLabors.FontsとSkiaSharpへ依存しません。

## 使い方

```csharp
using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;

using var engine = new ParleyTextEngine2D();
string latin = engine.AddFont(latinFontStream);
string japanese = engine.AddFont(japaneseFontStream);
var options = new ParleyTextOptions2D
{
    FontFamily = latin,
    FallbackFontFamilies = [japanese],
    FontSize = 24,
    WrappingLength = 480,
    LineHeight = 1.2f,
    Alignment = ParleyTextAlignment2D.Start,
    BaseDirection = ParleyTextDirection2D.Auto,
    Locale = "ja",
    FontFeatures = [new("liga", 1), new("kern", 1)],
};
TextLayout2D layout = engine.CreateLayout("Hello, 日本語！", options);

var canvas = new Canvas2D();
canvas.Begin();
canvas.DrawText(layout, new Vector2(20, 30));
DrawList2D list = canvas.End();
// 通常のRenderer2D.PrepareAsync → Record → Submitへ渡します。
```

`AddFont(Stream)`はstreamの現在位置から一つのTrueType/OpenType faceを読み、streamを所有しません。
`AddFont(ReadOnlySpan<byte>)`もあり、どちらも必要なデータを呼び出し中にコピーします。
返されたfamily名をFontFamilyまたはFallbackFontFamiliesへ指定します。
ブラウザーでもフォントを明示して登録し、OSのフォント一覧に依存しません。
フォントの取得を非同期に行う場合は、取得後に同期の`AddFont`と`CreateLayout`へ渡してください。

FontSizeは正のfiniteな論理emサイズです。既定16で、既定のWrappingLengthは正の無限大です。
有限の折り返し幅も正数を要求します。LineHeightはemに対する正のfiniteな倍率で、既定1.2です。
AlignmentはStart・End・Center・Justify、BaseDirectionはAuto・LeftToRight・RightToLeftを選べます。
FontFamilyがnullなら、最初に登録したfamilyを使います。
LocaleにはBCP 47のlanguage tagを指定でき、言語別のglyph選択や文字解析へ渡します。

生成した`TextLayout2D`はnative engineやフォントから独立したCPU snapshotです。
engineをDisposeした後も描画でき、[共通Text](../Lumyte.Graphics.Text/README.md)の
`DistanceFieldText2D.CreateAsync`へ渡してMSDF/MTSDF atlasを作ることもできます。
カラーglyphは距離場へ符号化せず、vector paint graphを保持します。

## スタイル・OpenType機能・可変軸

`TextRuns`ではfont・size・weight・italic・装飾・paintを範囲ごとに上書きできます。

```csharp
var accent = PaintGraph2D.LinearGradient(
    new Vector2(0, 0), new Vector2(150, 0),
    [new(0, new Color2D(0.1f, 0.3f, 1, 1)), new(1, new Color2D(0.8f, 0.1f, 0.5f, 1))]);
var styled = options with
{
    TextRuns = [new ParleyTextRun2D
    {
        Start = 0, End = 5, FontSize = 30, FontWeight = 700,
        Decorations = ParleyTextDecorations2D.Underline, Paint = accent,
    }],
};
TextLayout2D layout = engine.CreateLayout("Hello, 日本語！", styled);
```

Start/Endは**UTF-16のindex**で、Startを含みEndを含まない範囲です。
surrogate pairの途中を境界にすることはできません。
書記素や合字の途中でstyleを分割する指定は、上流のcluster処理に従います。
指定したUTF-16のコード単位ごとにpaintや装飾を分割することは保証しません。
SixLabors比較用アダプターの書記素indexとは異なるため、その範囲を直接流用しないでください。
Paintは段落全体の論理座標を使い、nullならlayoutの既定paintを継承します。
`CreateLayout`のpaint省略時は黒です。
FontWeightは1～1000、Italicはboolの上書きです。
これらは登録済みの実faceや可変軸でstyleを選択する指示です。
Fontiqueが提案する疑似太字・疑似斜体の合成は適用しないため、必要なfaceまたは軸を持つfontを登録してください。
DecorationsはUnderline・Strikethrough・Overlineの組み合わせをlayoutまたはrunへ指定できます。
装飾もfontのメトリクスからベクターとして作り、glyphの後に記録します。

`FontFeatures`は4文字のprintable ASCII OpenType tagと0～65535の値です。値0はそのfeatureを無効にします。
`FontVariations`は同じ形式のaxis tagとdesign座標を受け取り、shaping・輪郭・COLR paintへ同じinstanceを使います。

```csharp
var variable = options with
{
    FontVariations = [new("wght", 650), new("wdth", 90)],
};
```

軸やfeatureが作用するかどうかは、登録したフォントのtableに依存します。

## COLR v1

COLR glyphは、Skrifaのpaint traversalをsolid、linear/radial/sweep gradient、transform、clip、
groupとcompositeのevent列として受け取り、`PaintGraph2D`へ変換します。
最終描画は既存のDrawing2D shaderが行います。
CPAL paletteは`ColorPaletteIndex`で選び、0xFFFFのcurrentColorはlayoutまたはrunのpaintを参照します。
currentColorには半透明色やgradientも指定できます。

グリフの輪郭とカラーpaintは同じfont・variation座標から取り出します。
カラーglyph全体のbitmap化や、Skiaのnative assetを必要としません。
描画時にはDrawing2Dのpaint depth・命令数・geometry budgetも適用されます。

## glyphとclusterの情報

`CreateDetailedLayout`は、描画snapshotに加えてglyphの配置と元文字列のcluster範囲を返します。

```csharp
ParleyTextLayout2D detailed = engine.CreateDetailedLayout(text, options);
TextLayout2D layout = detailed.Layout;
foreach (ParleyGlyph2D glyph in detailed.Glyphs)
{
    // GlyphIdはFontId/FontFamilyのface内の識別子です。
    // Originは論理pixelのbaseline座標、Advanceは横方向の送りです。
    // Utf8Start/LengthとUtf16Start/Lengthを区別して使います。
}
```

Glyphsはvisual painter orderで、元文字列の範囲はlogicalなclusterを指します。
一つのclusterは複数のcode pointやglyphを含み得ます。glyph IDを文字コードとして扱わないでください。
この情報は描画snapshotと同様にengine解放後も使用できます。

## 寿命・スレッド・失敗

同じengineへの登録・レイアウト作成・Disposeは内部で直列化します。
複数の独立したengineは別々に所有できます。
呼び出し中はoptionsのlistや入力bufferを変更しないでください。
生成後に設定を変えても、既存snapshotには影響しません。

少なくとも一つのfontを登録してからlayoutを作成します。
登録前の作成はInvalidOperationException、未登録のfamilyや不正な設定はArgumentException系、
壊れたfont等のnative入力エラーはInvalidDataExceptionで伝えます。
Dispose後のengine操作はObjectDisposedExceptionになり、Dispose自体は繰り返せます。

試作の入力・出力には次の上限があります。共通のMSDF生成器とDrawing2Dのbudgetは別途適用されます。

| 対象 | 上限 |
| --- | --- |
| fontデータ | 一つにつき64 MiB、engineにつき1024登録 |
| 文字列 | UTF-8で1 MiB |
| スタイル範囲 | 4096 |
| glyph数 | layoutにつき262144 |
| COLR paint traversal・gradient stop | glyphにつき各4096 |
| native snapshot | path segment 2000000、paint event 262144 |

## C ABIとビルド

Rust側は[native/lumyte_text_parley](../../../native/lumyte_text_parley)、
ビルド用targetsと.NETのFallout runnerは[tools/text-parley](../../../tools/text-parley/README.md)にあります。
依存グラフはCargo.lockへ記録し、`--locked`でビルドします。

ビルドの入口はリポジトリの`global.json`に対応する.NET SDKです。
[Fallout](https://github.com/Fallout-build/Fallout)を通常のC# console projectから使うため、
Falloutのglobal toolやPythonの事前インストールは不要で、ビルド入口にshell scriptを使いません。
Rustのコンパイルにはhost用のnative linkerも必要です。WindowsではMSVC/Windows SDK、
LinuxではC linkerと開発用ライブラリ、macOSではCommand Line Tools等を用意してください。

リポジトリルートで次のコマンドを実行すると、`ParleyNative.targets`がFalloutのNative targetを呼び、
Rust成果物とライセンス表示を出力先へコピーします。

```sh
dotnet build src/Graphics/Lumyte.Graphics.Text.Parley
```

Rust toolchainはnative用の1.99.1を使います。利用できるtoolchainがなければ、
runnerが`artifacts/text-parley/tools`以下へビルド用のRustupとtoolchainを準備します。
既存の設定・cacheも利用でき、globalなPATHやshell profileを書き換えません。
初回のNuGet復元やtoolchain取得にはネットワーク接続が必要です。

native成果物だけを作る場合や、toolchainを先に準備する場合もdotnetから実行できます。

```sh
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target SetupNative --root .
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target Native --root .
```

現在のnative targetはhostのOS・architecture向けにbuildします。
別RIDへのpublishに合わせたcross compileは未整備です。
実行先と同じOS・architectureの環境でnative成果物を用意してください。

既定のnative成果物は`artifacts/text-parley/native`です。
既に用意したライブラリを使う場合は、`LumyteBuildParleyNative=false`と
`LumyteParleyNativePath`を指定できます。
付属するRust標準ライブラリの表示は`LumyteParleyRustNoticesPath`で指定します。
これらはソースツリーでの試作用targetsであり、
各RIDの成果物を含む完成した配布NuGetを意味しません。
Parleyと比較用SixLaborsのアダプタープロジェクトは、配布を整えるまでIsPackable=falseです。

`build.rs`がRustの`extern "C"`と`repr(C)`から
`Interop/NativeMethods.g.cs`を生成します。生成ファイルを手作業で変更しません。
境界はopaqueなengine/layout handleとblittableな配列に限定し、
Parleyの内部Rust型や、文字ごとのmanaged callbackをC#へ出しません。
csbindgenはbuild時の依存で、生成器を実行時に配布する必要はありません。

native layoutが所有するglyph・path・paint・stopの配列をC#へコピーしてから、Rust側の解放関数で返します。
Rustで確保したメモリをmanaged側のfreeで解放しません。
statusとUTF-8のエラーをC#の例外へ変換し、RustのunwindをC ABIへ通しません。
WASMは`panic=abort`でbuildするため、予期しないnative panicからの回復は保証しません。

Browser WASMでは、.NETのEmscriptenとABIを合わせたRust staticlibを`dotnet.wasm`へリンクします。
検証用のRustはnightly-2026-10-20とrust-srcです。
runnerが必要なtoolchainを準備し、同じEmscriptenで標準ライブラリも再ビルドします。
.NETのwasm-tools workloadは別途用意します。

Emscripten自身は内部でPythonを使います。runnerはSDK同梱のPythonを優先し、
同梱されていない環境ではSHA-256を照合した専用のPythonを`artifacts/text-parley/tools`以下へ準備します。
PATH上のPythonには依存しません。ビルド前に解決したinterpreterを.NET側のEmscriptenにも渡します。

Browserアプリは`ParleyBrowser.targets`をimportし、ParleyへのProjectReferenceで
`LumyteBuildParleyNative=false;LumyteParleyBrowser=true`を渡します。
この指定でnative cdylibのbuildを止め、生成bindingのmodule名を`liblumyte_text_parley`へ切り替えます。
Browserではこの名前を使い、.NETの静的P/Invoke tableからリンク済みのライブラリを解決します。
`wasm32-unknown-unknown`のコンパイル成功と、この.NET Browser接続の成功を区別してください。
具体的なpublish・起動手順は[Parley Browserサンプル](../../../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md)を参照してください。

## 対応範囲と配布条件

この試作は、Parleyのすべてのレイアウト・編集APIを公開するものではありません。
Pango markup、hit test・caret・selection、縦組みの組版保証、SVG glyphやbitmap emojiの描画は今回の範囲外です。
必要なglyphとtableを持つフォントをアプリが供給します。
Parley上流には、改行位置に応じた合字・筆記体の再shapingなど未完了の組版処理もあります。
複雑な文字体系の強制改行や禁則処理まで、Pangoと同じ品質を保証しません。
性能・配布サイズや、各OS/architectureの動作は個別に評価します。

C#からnativeを通す回帰テストと、Rust shimのテストは次のように実行できます。

```sh
dotnet test tests/Graphics/Lumyte.Graphics.Text.Parley.Tests -c Release
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target TestRust --root .
```

toolchainやビルドtargetの引数は[ビルド手順](../../../tools/text-parley/README.md)を参照してください。

2026-10-27にLinux x64でC#の90ケース、Rustの13テストと警告をエラーにしたclippyが成功しました。
分離後のSixLabors比較用テスト91ケースも成功しています。
[Browserサンプル](../../../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md)は
.NET Browser WASMのpublish・静的リンク後、headless ChromiumのWebGPUで実行しました。
合字、日本語fallback、bidi cluster、折り返し、MSDF/MTSDFの4倍拡大時の穴、
COLR v1のgradient・multiplyと半透明／gradient currentColorをGPU読み戻しで確認しています。
engine解放後のsnapshot描画も確認しました。

Falloutへの移行後も、MSBuild経由のnative buildとC#統合テスト90件が成功しました。
BrowserはPATHからPythonとglobalなCargoを外してpublishし、
その成果物のChromium / WebGPU描画も確認しています。
ビルド入口のプロセス統合テスト等は[ビルド手順の確認範囲](../../../tools/text-parley/README.md#確認した範囲)を参照してください。

2026-10-27のCIでは、LinuxとWindowsのx64 / arm64でnativeビルドとParleyの90ケースが成功しました。
macOSでの実行、配布サイズと性能の比較は未検証です。

主な依存はParley 0.12、Skrifa 0.44、build時のcsbindgen 1.9.8です。
Parley・Fontique・Fontationsは主にMITまたはApache-2.0、HarfRustとcsbindgenはMIT、
ICU4Xと同梱データにはUnicode License系の条件があります。
Cargo.lockのregistry依存については[第三者ライセンス表示](../../../native/lumyte_text_parley/THIRD-PARTY-NOTICES.md)を同梱します。
Fallout runnerは使用したtoolchainの`COPYRIGHT-library.html`を
`RUST-STANDARD-LIBRARY-NOTICES.html`として成果物へコピーし、nativeとBrowserのtargetsが出力・publishへ同梱します。
Browserの`wwwroot`には、このHTMLと`THIRD-PARTY-NOTICES.md`、
`DISTANCE-FIELD-THIRD-PARTY-NOTICES.md`を配置し、フォントの表示も同梱します。
platform runtimeを別途配布する場合は、そのruntimeの表示も含めてください。
[Parley](https://github.com/linebender/parley)、[Fontations](https://github.com/googlefonts/fontations)、
[HarfRust](https://github.com/harfbuzz/harfrust)、[ICU4X](https://github.com/unicode-org/icu4x)、
[csbindgen](https://github.com/Cysharp/csbindgen)を参照してください。

設計は[GRAPHICS-0024](../../../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)を参照してください。
