# Parleyのビルド

Parleyのnative / Browser WASM成果物は、C#の[Fallout](https://github.com/Fallout-build/Fallout) runnerで作ります。
入口はリポジトリの`global.json`に対応する.NET SDKです。
Falloutのglobal tool、シェル用のbootstrap script、PATH上のPythonは必要ありません。
対象はParleyのビルドで、既存の共通開発環境セットアップの手順は[開発環境](../../docs/development-environment.md)を参照してください。
Emscripten用のPython解決は、SixLabors比較用の文字描画サンプルとも共有します。

## 通常のビルド

リポジトリルートから実行します。

```sh
dotnet build src/Graphics/Lumyte.Graphics.Text.Parley
dotnet test tests/Graphics/Lumyte.Graphics.Text.Parley.Tests -c Release
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target TestRust --root .
```

MSBuildの`ParleyNative.targets`がFalloutのNative targetを呼びます。
必要なRust toolchainを解決し、Cargo、csbindgenの生成、成果物とnoticeのコピーを行います。
手動でnative成果物だけを作る場合も同じtargetを使えます。

```sh
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target Native --root .
```

Rustのhost用build処理にはOSのnative linkerが必要です。
WindowsではMSVC/Windows SDK、LinuxではC linkerと開発用ライブラリ、macOSではCommand Line Tools等を用意してください。
native成果物はhostのOS・architecture向けです。別RIDへのcross compileは未整備です。

## Browser WASM

同じリポジトリの.NET workloadを用意してからpublishします。

```sh
dotnet workload install wasm-tools --version 10.0.401
dotnet publish samples/Lumyte.Graphics.Text.Parley.Browser.Sample -c Release
```

`ParleyBrowser.targets`は.NET SDKのEmscripten・cache・NodeのパスをFalloutへ渡します。
共通の`EmscriptenPython.targets`が`PrepareWasmTools`でPythonを解決し、
同じinterpreterをRust側と.NET側のEmscriptenへ設定します。
`Wasm`は.NET側と同じEmscriptenでRust標準ライブラリも再構築し、static libraryを作ります。
ABI・LLVM featureの互換設定、LTO無効化、静的module名`liblumyte_text_parley`はtargetsとrunnerで管理します。

Emscripten自身は内部でPythonを使います。`--python`で指定したinterpreter、SDK同梱のPython、
ビルド用に取得する専用のPythonの順で解決し、PATH上のPythonを探しません。
Windowsのwasm-toolsに同梱されるSDKにPythonが含まれる場合は、そのinterpreterを使います。
専用runtimeはCPython 3.12.16 / python-build-standaloneの20261026 releaseで、
asset URLとSHA-256を[PythonRuntimes.json](Lumyte.Text.Parley.Build/PythonRuntimes.json)へ固定しています。
取得・照合・展開は.NETで行い、runtimeに含まれるライセンス表示もcacheへ保持します。
これはビルド用のruntimeで、アプリのWASMへPythonを組み込む構成ではありません。

描画サンプルの起動・自動検証は[Browserサンプル](../../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md)を参照してください。

## Targetと引数

globalなFallout CLIのインストールは不要です。
runnerは`Fallout.Build`をNuGetから復元する通常の.NET console projectです。

```sh
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target SetupNative --root .
dotnet run --project tools/text-parley/Lumyte.Text.Parley.Build -- --target SetupWasm --root .
```

| Target | 処理 |
| --- | --- |
| Native | Rust 1.99.1でhost向けcdylibをbuildし、成果物とRust標準ライブラリのnoticeをコピー |
| Wasm | nightly-2026-10-20とrust-srcで.NET互換のstaticlibをbuildし、成果物とnoticeをコピー |
| SetupNative | Native用のRust toolchainを先に準備 |
| SetupWasm | Wasm用のRust toolchainとrust-srcを先に準備。wasm-tools workloadの導入は別途実行 |
| TestRust | native用toolchainを解決し、Rust shimのテストを`cargo test --locked`で実行 |
| PrepareWasmTools | Emscripten用のPythonを解決し、`--python-path-file`へそのパスを書き出す |

| 引数 | 用途 |
| --- | --- |
| `--root` | リポジトリルート。例では`.`を指定 |
| `--output` | 成果物の出力先。既定は`artifacts/text-parley/native`、`wasm`または`tests` |
| `--toolchain` | 対象targetのRust toolchainを上書き |
| `--emsdk` | .NETの`EmscriptenSdkToolsPath`。手動のWasm実行で指定 |
| `--cache` | .NETの`EmscriptenCacheSdkCacheDir`。手動のWasm実行で指定 |
| `--node` | .NET側で使用するNode実行ファイルの完全パス |
| `--python` | Emscripten用Pythonを明示。未指定ならSDK同梱または専用runtimeを使う |
| `--python-path-file` | PrepareWasmToolsが解決したPythonパスの書き出し先 |
| `--cargo` / `--rustc` / `--rustup` | 使用するRust実行ファイルを明示する場合の上書き |

通常の`dotnet build`と`dotnet publish`では、SDK固有のパスを手作業で入力する必要はありません。
MSBuildから設定を上書きする場合は、次のpropertyを使用できます。

| Property | 用途 |
| --- | --- |
| `LumyteParleyDotNetHost` | runnerを起動するdotnet実行ファイル。既定は使用中のSDKのhost |
| `LumyteParleyNativeToolchain` / `LumyteParleyWasmToolchain` | 対応するRust toolchain |
| `LumyteParleyNativeOutput` / `LumyteParleyWasmOutput` | 対応する成果物・Cargo cacheの出力先 |
| `LumyteParleyPythonPath` | Emscriptenに渡すPython実行ファイルの明示指定 |
| `LumyteBuildParleyNative` / `LumyteBuildParleyWasm` | `false`で対応するRust成果物の再生成を止める |

既製のnative成果物の使用は[Parley README](../../src/Graphics/Lumyte.Graphics.Text.Parley/README.md)を参照してください。

## Cacheと再現性

成果物とビルド用toolは`artifacts/text-parley`以下へ置きます。
RustとPythonの専用cacheは`tools`以下に置き、globalなPATHやshell profileを書き換えません。
既存のRust環境を使う場合は、設定済みの`CARGO_HOME`・`RUSTUP_HOME`等を引き継ぎます。
Rustupのbootstrapも[固定したURLとSHA-256](Lumyte.Text.Parley.Build/RustBootstrap.json)を照合します。
NuGet復元と、まだcacheされていないtoolchain・runtimeの取得にはネットワーク接続が必要です。

Falloutは`Fallout.Build` 10.4.0を使い、NuGetの解決結果をlock fileへ記録します。
target graphと実行に必要なパッケージだけを選び、アプリのruntime依存へ追加しません。
MSBuildから起動するrunnerの中間成果物は、呼び出し元projectごとに`artifacts/text-parley/build`以下へ分けます。
Falloutの一時ファイルは無視対象の`.fallout/temp`へ置きます。
Rustの依存は`native/lumyte_text_parley/Cargo.lock`を使って`--locked`でbuildします。
toolchainや依存の更新時にはlockと互換設定を見直し、native / WASMの回帰検証を行います。

Rustで確保したメモリの所有権や文字描画の契約は、このビルド入口の変更では変わりません。
設計は[GRAPHICS-0019](../../docs/adr/graphics/GRAPHICS-0019-text-rendering.md)を参照してください。

## 確認した範囲

2026-10-27にLinux x64で、MSBuildからFallout runnerを起動してNative targetを実行し、
Cargo・csbindgen生成・native成果物とnoticeのコピー、C#統合テスト90件の成功を確認しました。
TestRust target経由のRustテスト13件、runnerの.NETプロセス統合テスト9件、
runnerの警告をエラーにしたbuildも成功しています。
プロセス統合テストでは、PythonのないPATH、空白や日本語を含むパス、環境変数の継承、失敗の伝播を確認しています。
専用Pythonについては、固定archiveの取得・SHA-256照合とCPython 3.12.16の起動を確認しました。

実際のBrowser WASMも、PATHからPythonとglobalなCargoを外してpublishしました。
専用Pythonを.NET側とRust側のEmscriptenへ渡したビルドが成功し、
その成果物をheadless Chromium / WebGPUで実行して23,606個のink pixelを読み戻しました。
COLR v1、MSDF/MTSDF、currentColor等の描画検証も成功しています。
比較用SixLabors Browserサンプルも、同じPythonのないPATHでEmscriptenのcompile・link・wasm-optを含むpublishが成功しました。

tool取得のmetadataはLinux・macOS・Windowsのx64 / arm64を持ちますが、
これは各環境でのビルド・実行検証済み一覧ではありません。
