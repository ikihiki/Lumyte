# 共通ビルド

ビルド・テスト・publish・NuGet pack・検証は、[Fallout](https://github.com/Fallout-build/Fallout)の共通runnerから実行します。
入口は[global.json](../global.json)に対応する.NET SDKです。Falloutのglobal install、PATH上のPython、ビルド用shell scriptは不要です。
compiler・Slang・mise環境の準備は[開発環境](../docs/development-environment.md)を参照してください。
Linuxの初期セットアップも利用可能な.NET 10 SDKを前提にし、JSON読み取りやNative smokeにシステムPythonを使用しません。

## 通常の実行

リポジトリルートから実行します。引数なしの既定targetは`Compile`です。

```sh
dotnet run --project build -c Release -- --target Compile
dotnet run --project build -c Release -- --target Test
dotnet run --project build -c Release -- --target Publish
dotnet run --project build -c Release -- --target Pack
```

`Test`と`Pack`は必要なrestore・compileから実行します。`Publish`の既定projectはParleyのBrowserサンプルです。
対象やconfigurationも同じ入口で指定できます。

```sh
dotnet run --project build -c Release -- --target Compile --configuration Debug
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Text.Parley.Tests
dotnet run --project build -c Release -- --target Publish --project samples/Lumyte.Graphics.Text.Parley.Browser.Sample --output artifacts/text-browser
dotnet run --project build -c Release -- --target Pack --project src/Graphics/Lumyte.Graphics.Text.Parley
dotnet run --project build -c Release -- --target Test --no-build --no-restore
```

`dotnet run`の`-c Release`はrunner自身の構成、`--`以降の`--configuration`は対象projectの構成です。
`--no-build --no-restore`では、同じconfigurationで作成済みの成果物を使用してください。
Parleyのpackには、後述する4 RID分のnative資産が必要です。
手元のhost以外の資産がない状態で、他のライブラリだけをpackする場合は`--skip-parley-package`を指定します。

```sh
dotnet run --project build -c Release -- --target Pack --skip-parley-package
```

## 公式構成と責務

[公式の推奨構成](https://github.com/Fallout-build/Fallout/blob/main/docs/website/01-getting-started/02-setup.md)に従います。

```text
.fallout/
  parameters.json             root marker・共通パラメーター
  build.schema.json           生成されるschema
build/
  _build.csproj               実行するconsole project
  Build.cs                    共通CLIとbuild・test・pack
  Build.*.cs                  機能別の工程
  Directory.Build.props       親MSBuild設定の自動importを遮断
  Directory.Build.targets
  Targets/                    各projectのMSBuild hook
```

CIとmiseは同じtargetを呼びます。CIにはOS／architecture、権限境界、artifact保存を残し、実行するコマンド列はC#側で管理します。
MSBuild、Cargo、Slang、CMakeは各工程のworkerとして、コンパイルと増分判定を担当します。

`_build.csproj`はsolutionへ登録し、通常のsolution buildでは自身を対象から外します。
MSBuild hookはleaf targetだけを呼び、solution全体へ再入しません。
実行中のrunnerを利用できる場合は再利用し、それ以外のhook用出力は呼び出し元projectごとに分けます。
入れ子のleaf実行ではFalloutの状態rootも`artifacts/build/invocations/<id>`へ分け、
`--repository-root`で実際のcheckoutを渡します。これにより`.fallout/temp`のログが競合せず、Cargoとcacheは同じリポジトリを参照できます。

## Target

| Target | 処理 |
| --- | --- |
| `Restore` / `Compile` / `Test` / `Clean` | 対象projectまたはsolutionの復元・build・test・clean。TestはTRXを保存 |
| `Publish` | 指定projectをpublish。既定はParley Browserサンプル |
| `Pack` | 指定project、または`src/`のpack可能なprojectをNuGet化 |
| `CheckFormat` | 復元済みのsolutionへdotnet formatを適用し、変更せず検査 |
| `CheckMarkdown` / `FormatMarkdown` | Markdownのlint／自動修正 |
| `CheckNativeFormat` / `FormatNative` | C/C++の書式検査／自動修正 |
| `VerifyEnvironment` / `NativeSmoke` | 共通環境の検証／Slang・CMake・vcpkg・Native NuGet・P/Invoke・software GPUの検証 |
| `Direct3D12Native` | 選択したAgility SDK・MSVC・CMakeによるDirect3D 12 native DLLのビルド。`--agility-channel Stable/Preview` と復元済み `--agility-sdk-root` を使用 |
| `SetupWasm` / `TestWasm` | wasm-toolsの導入／Graphics・Parley Browserのpublishと実WebGPU検証 |
| `TestText` / `TestRust` | 共通Text・Parley／Rust shimのテスト |
| `TestSignals` / `BenchmarkSignals` | Signalsの単体テスト・sample・benchmark動作確認／BenchmarkDotNet測定結果の保存 |
| `TestBrowserContracts` / `TestWindowing` | Graphics・AudioのJavaScript契約／Linux X11・Browser Windowingの検証 |
| `TestBrowserAudio` | Browser Audioをpublishし、専用Python／Playwrightで検証 |
| `TestDiagnosticsProcesses` / `TestDiagnosticsUi` | 診断プロセス通信／診断サーバーとWeb UIの検証 |
| `DiagnosticGallery` | 検証結果から診断galleryを生成 |
| `Samples` / `TestAot` | CPU・Audio等のsample／NativeAOT publishと実行 |
| `PipelineBenchmark` / `PublishWindowSample` | Vulkan benchmarkの保存／単一Windowサンプルのpublish |
| `Ci` | 共通のCI向け検証target群を実行 |
| `ArchiveCiBuild` / `VerifyCiBuild` | ビルド済みsolutionをtar化／後段runnerとの互換性と主要出力を検証 |

X11には`xvfb-run`、Browser検証にはNodeとChromium／Chromeが必要です。`CHROME`で実行ファイルを指定できます。
OS別の実行対象は[workflow](../.github/workflows/composition.yml)で確認できます。
WindowsのNative smokeを通常ユーザーで起動する処理や、GitHub Pagesの配信権限は分離したままです。

## CIのcacheと成果物共有

CIは4環境の`build` jobでRelease solutionをビルドし、同じrunner image・architectureの
`validate` jobへ成果物を渡します。Linux x64の`quality` jobも同じ成果物を使い、
sample・NativeAOT・pack・Debug build・lintを並列に検証します。
`package` jobは`validate`と`quality`の成功後に全RIDのParley資産をまとめます。

[setup-ci](../.github/actions/setup-ci/action.yml)はNuGet、miseのインストール済みtool、
vcpkgのdownload／binary cache、Linux DXC、ParleyのCargo増分ビルドを復元します。
NuGetの保存先は`artifacts/dev-env/nuget-packages`です。
NuGet・mise・vcpkg・Cargoのキーはrunner image・OS・architectureとSDK／依存定義を含み、NuGetとCargoのみ互換範囲のprefix fallbackを使います。
キャッシュは成功したjobの終了時に保存されます。miseのキーはtool／setup入力が変わると更新され、
後段で追加したWASM workloadは既存のcacheへ追記されません。
cache hitでもsetupは実行し、manifest・環境設定・OS依存関係を準備します。

`ArchiveCiBuild`は`artifacts/ci/build.tar`を作成します。
対象は`build`・`src`・`tests`・`samples`・`benchmarks`の`bin`／`obj`、
Parleyのnative資産・notice・package資産、Direct3D 12のnative DLLです。
tool cache、Cargo／CMakeの中間出力、ログは含みません。
tarで実行権限とsymlinkを保持し、`build-<runner image>-<architecture>-Release` artifactとして7日間保存します。
Browser publishがdesktop向け出力を変更する前に保存するため、後段は同じdesktop成果物から開始できます。

[restore-ci-build](../.github/actions/restore-ci-build/action.yml)は同じworkflow runのartifactを展開し、
`VerifyCiBuild`でcheckoutの絶対パス・OS／architecture・configurationと主要出力を確認します。
`obj`やテストfixtureに絶対パスが含まれるため、producerとconsumerは同じrunner label・checkout位置を使います。
その後`Restore`でNuGet依存を補い、cache miss時にも動作するようにします。
通常のテストではrunner自身と対象projectの両方に`--no-build --no-restore`を指定します。
WASM・NativeAOTのpublishとDebug buildは別構成のため、それぞれ後段でビルドします。

## ParleyのNative／WASM

| Target | 処理 |
| --- | --- |
| `ParleyNative` | host用のcdylibとRust標準ライブラリのnoticeを生成 |
| `ParleyWasm` | .NETのEmscriptenとABIを合わせたstaticlibを生成 |
| `SetupParleyNative` / `SetupParleyWasm` | native用／WASM用Rust toolchainとrust-srcを準備 |
| `PrepareWasmTools` | Emscripten用Pythonを解決し、MSBuildへパスを渡す |

native用はRust 1.99.1、WASM用はnightly-2026-10-20を使います。必要なtoolchainがなければrunnerが取得します。
hostのOS・architecture向けにbuildし、別RIDへのcross compileは行いません。
native用NuGetには、Linux／Windowsのx64／arm64の4 RID分をまとめます。
各hostのMSBuildは、ライブラリと使用したRust標準ライブラリのnoticeを次の領域へstageします。

```text
artifacts/text-parley/package/
  runtimes/
    linux-x64/native/liblumyte_text_parley.so
    linux-arm64/native/liblumyte_text_parley.so
    win-x64/native/lumyte_text_parley.dll
    win-arm64/native/lumyte_text_parley.dll
    <rid>/native/RUST-STANDARD-LIBRARY-NOTICES.html
```

`Pack`は全4 RIDのライブラリとnoticeを検証し、一つでも不足すると失敗します。
同じhostのライブラリを別RID名へコピーして代用しません。
CIのpackage jobは全4環境の検証成功を条件にnative artifactを集め、desktop用managed出力と組み合わせて`parley-nuget` artifactを生成します。
NuGet内では`runtimes/<rid>/native`と`licenses/THIRD-PARTY-NOTICES.md`に配置します。

このNuGetはnative用です。Browserはソースprojectを参照し、以下のMSBuild／WASM経路でbuildします。
Browser向けNuGetからの静的リンクは、今回のパッケージには含みません。

MSBuildの`ParleyNative.targets`と`ParleyBrowser.targets`も同じtargetへ接続します。
BrowserではSDKのEmscripten・cache・Node・PythonをRust側へ渡し、Rust標準ライブラリも再構築します。
module名`liblumyte_text_parley`を.NETの静的P/Invoke tableへリンクします。
ABI・LLVM feature、LTO、post-link validatorの互換設定はtargetsとrunnerで管理します。
具体的なBrowser構成は[サンプル](../samples/Lumyte.Graphics.Text.Parley.Browser.Sample/README.md)を参照してください。

Emscriptenは内部でPythonを使います。明示指定、SDK同梱runtime、専用runtimeの順で解決し、PATH上のPythonを探しません。
専用runtimeはCPython 3.12.16 / python-build-standalone 20261026のarchiveです。
[PythonRuntimes.json](PythonRuntimes.json)のURLとSHA-256を照合して.NETで取得・展開します。
Pythonはbuild用で、アプリのWASMには組み込みません。

## パラメーターとcache

| 引数 | 用途 |
| --- | --- |
| `--project` / `--solution` | 対象project／solution。solutionの既定は`Lumyte.slnx` |
| `--configuration` | 対象の構成。既定Release |
| `--runtime` | publish／packの対象RID |
| `--output` | publish／pack、またはParley leafの出力先 |
| `--no-build` / `--no-restore` | 既存のbuild／restore結果を使用 |
| `--skip-parley-package` | 4 RID分を集めず、Parley以外のライブラリをpack |
| `--dotnet-path` | 子プロセスに使うdotnetの明示指定 |
| `--repository-root` | MSBuild leaf workerが独立した状態rootを使う場合の実checkoutパス |
| `--toolchain` | Parley leaf targetのRust toolchainを上書き |
| `--emsdk` / `--cache` / `--node` | .NETのWASM toolchainパス。通常はMSBuildが渡す |
| `--python` / `--python-path-file` | Pythonの明示指定／`PrepareWasmTools`の解決結果の出力先 |
| `--cargo` / `--rustc` / `--rustup` | Rust実行ファイルの明示指定 |

nativeのOS linker、Slang、.NET workloadは別途必要です。
WindowsはMSVC／Windows SDK、LinuxはC linkerと開発用ライブラリ、macOSはCommand Line Tools等を使用します。

共通の生成物は`artifacts/`、NuGetは`artifacts/nuget`へ出力します。
Parleyのnative／WASM成果物は`artifacts/text-parley`、Rust・Pythonの専用cacheは`artifacts/build/tools`へ保存します。
Falloutの一時ファイルはrootごとの`.fallout/temp`へ置きます。
MSBuild leafの状態rootは`artifacts/build/invocations`以下です。
globalなPATH、shell profile、GPUドライバー登録は変更しません。

Fallout.Buildは10.4.0で固定し、[packages.lock.json](packages.lock.json)に解決結果を記録します。
Rustも[Cargo.lock](../native/lumyte_text_parley/Cargo.lock)に従い`--locked`でbuildします。
Rustupのbootstrapは[RustBootstrap.json](RustBootstrap.json)の公式URLとSHA-256で確認します。
初回の復元・tool取得にはネットワーク接続が必要です。

設計は[ADR-0004](../docs/adr/0004-build-orchestration.md)、文字描画は[GRAPHICS-0024](../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)を参照してください。
