# Parley / csbindgen / WebAssemblyの文字描画例

Parleyの段落レイアウトとSkrifaの輪郭・COLR paintを、csbindgenで生成したC ABI経由で読みます。
描画は既存のDrawing2D / Browser WebGPUを使います。
文字描画の標準経路を、同梱フォントとGPU読み戻しで検証します。

リポジトリルートから実行します。

```sh
dotnet run --project build -c Release -- --target Publish --project samples/Lumyte.Graphics.Text.Parley.Browser.Sample --configuration Release
```

buildは.NETの[Fallout runner](../../build/README.md)を使い、
必要なRust toolchainと、Emscripten内部で使うPythonをビルド用の領域へ準備します。
Falloutのglobal toolや、PATH上のPythonは前提にしません。
Rustのhost用build処理にはOSのnative linkerが必要です。

公開された`wwwroot`をHTTPサーバーで配信し、WebGPU対応ブラウザーで開けます。
自動検証を実行する場合はNode.jsとChromiumを用意し、次のコマンドを使います。

```sh
dotnet run --project build -c Release -- --target TestWasm
```

Chromiumの別の実行ファイルは`CHROME`環境変数で指定できます。
`LUMYTE_SCREENSHOT`にPNGの保存先を指定すると、検証成功時の画面も保存します。

## Rust / .NETの接続

Browser用のRust static libraryは、アプリと同じ.NET wasm-toolsのEmscriptenを使います。
Rustの標準ライブラリも`nightly-2026-10-20`と`rust-src`で再ビルドし、`panic=abort`にします。
古いEmscriptenとのLLVM bitcode互換を要求しないよう、WASMビルドではLTOを無効にします。
同梱Binaryenが対応するtarget featureへ揃えるため、Rust stdと全依存で
`bulk-memory-opt`と`call-indirect-overlong`を無効にします。
また、.NETのpost-link validatorへRustが使う標準WASM featureを明示して検証します。
生成されたarchiveは`artifacts/text-parley/wasm/liblumyte_text_parley.a`です。

ParleyのProjectReferenceは`LumyteParleyBrowser=true`を渡します。
これにより生成されたP/Invokeが静的module名`liblumyte_text_parley`を参照します。
この.NET runtimeでは`__Internal`が動的ロードの特別扱いとなるため、archiveに対応する名前を指定します。
`ParleyBrowser.targets`がarchiveを`NativeFileReference`として.NET runtimeへ静的リンクします。
ビルド前処理は[FalloutのParleyWasm target](../../build/README.md)から再実行できます。
`PrepareWasmTools`がPythonを解決し、同じinterpreterをRust側と.NET側のEmscriptenへ渡します。

## 検証する内容

- `liga`の有効・無効によるglyph数の差。
- 明示登録した日本語フォントへのfallback。
- bidiの見た目順とUTF-16 source cluster、段落の折り返し。
- 直接描画とMSDF / MTSDF 4倍拡大のOの穴を、GPU読み戻しで確認。
- COLR v1のグラデーション・multiply合成を、ベクターpaint graphから描画。
- 半透明とグラデーションの`currentColor`を、期待する線形色合成値と比較。
- 28種類すべてのcompositeを、独立したsource / backdropとclip maskの内外で参照値と比較。
- 退化gradient、空・重複・単一stop、Repeat / Reflectのゼロspan、符号付きradial半径等の境界条件。
- engineを破棄した後も、作成済みlayoutを描画できること。

フォントはこのsampleの`Fonts`とParleyテストのfixtureを参照します。
DejaVu、Noto Sans JP、Noto Sans Khmer、カラーフォントfixtureのライセンスを`wwwroot`へ同梱します。
Rustの標準ライブラリ、Parley依存、SeasonMSDFのnoticeも`wwwroot`へ同梱します。
日本語フォントはサンプル用の文字だけを含むsubsetです。

2026-10-27にLinux x64で、リポジトリ全体のFallout `Publish` targetからのpublishと、
headless Chromium / WebGPU検証が成功しました。
SDK / workload setは10.0.401、WASM runtimeは10.0.12です。
.NET側とRust側のEmscriptenには、専用cacheのPythonを渡しています。
900×1700のGPU読み戻しで49,544個のink pixelを確認し、上段の文字panelは23,606個でした。
下段のbackend panelでは全28合成演算・clip・gradient境界条件を参照値と比較しています。
BrowserのGPU実行確認はLinux x64です。

公開APIと制約は[Parleyパッケージ](../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md)を参照してください。
