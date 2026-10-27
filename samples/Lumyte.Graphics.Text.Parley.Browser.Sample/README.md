# Parley / csbindgen / WebAssemblyの文字描画例

Parleyの段落レイアウトとSkrifaの輪郭・COLR paintを、csbindgenで生成したC ABI経由で読みます。
描画は既存のDrawing2D / Browser WebGPUを使います。
このsampleはSixLabors.FontsとSkiaSharpを参照しません。

リポジトリルートから実行します。

```sh
dotnet workload install wasm-tools --version 10.0.401
dotnet publish samples/Lumyte.Graphics.Text.Parley.Browser.Sample -c Release
```

buildは.NETの[Fallout runner](../../tools/text-parley/README.md)を使い、
必要なRust toolchainと、Emscripten内部で使うPythonをビルド用の領域へ準備します。
Falloutのglobal toolや、PATH上のPythonは前提にしません。
Rustのhost用build処理にはOSのnative linkerが必要です。

公開された`wwwroot`をHTTPサーバーで配信し、WebGPU対応ブラウザーで開けます。
自動検証を実行する場合はNode.jsとChromiumを用意し、次のコマンドを使います。

```sh
node samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs
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
ビルド前処理は[FalloutのWasm target](../../tools/text-parley/README.md)から再実行できます。
`PrepareWasmTools`がPythonを解決し、同じinterpreterをRust側と.NET側のEmscriptenへ渡します。

## 検証する内容

- `liga`の有効・無効によるglyph数の差。
- 明示登録した日本語フォントへのfallback。
- bidiの見た目順とUTF-16 source cluster、段落の折り返し。
- 直接描画とMSDF / MTSDF 4倍拡大のOの穴を、GPU読み戻しで確認。
- COLR v1のグラデーション・multiply合成を、ベクターpaint graphから描画。
- 半透明とグラデーションの`currentColor`を、期待する線形色合成値と比較。
- engineを破棄した後も、作成済みlayoutを描画できること。

フォントは既存sampleとテストのfixtureを参照します。
DejaVu、Noto Sans JP、Noto Sans Khmer、カラーフォントfixtureのライセンスを`wwwroot`へ同梱します。
Rustの標準ライブラリ、Parley依存、SeasonMSDFのnoticeも`wwwroot`へ同梱します。
日本語フォントはサンプル用の文字だけを含むsubsetです。

2026-10-27にLinux x64で、PATHからPythonとglobalなCargoを外した状態でpublishが成功しました。
.NET側とRust側のEmscriptenには専用cacheのPythonを渡しています。
この成果物のheadless Chromium / WebGPU検証も成功し、23,606個のink pixelを読み戻しました。
他OS・architectureでの実行確認は未実施です。

公開APIと制約は[Parleyパッケージ](../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md)を参照してください。
