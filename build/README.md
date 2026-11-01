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
| `TestBrowserContracts` / `TestWindowing` | Graphics・Audio・WindowingのJavaScript契約／Linux X11の検証 |
| `TestBrowserAudio` | Browser Audioをpublishし、専用Python／Playwrightで検証 |
| `TestDiagnosticsProcesses` / `TestDiagnosticsUi` | 診断プロセス通信／診断サーバーとWeb UIの検証 |
| `DiagnosticGallery` | 検証結果から診断galleryを生成 |
| `Samples` / `TestAot` | CPU・Audio等のsample／NativeAOT publishと実行 |
| `PipelineBenchmark` / `PublishWindowSample` | Vulkan benchmarkの保存／単一Windowサンプルのpublish |
| `CiNative` | solutionのrestore・build・単体テスト |
| `CiBrowser` | JavaScript契約・WebGPU・Browser Audio。Wasm toolsの導入を共有 |
| `CiDistribution` | sample実行・NuGet pack |
| `CiQuality` | C#・C/C++・Markdownの書式検査 |
| `Ci` | 上記4カテゴリとRustテスト。個別Textテストはsolutionテストに含まれる |
| `ArchiveCiBuild` / `VerifyCiBuild` | ビルド済みsolutionをtar化／後段runnerとの互換性と主要出力を検証 |

X11には`xvfb-run`、Browser検証にはNodeとChromium／Chromeが必要です。`CHROME`で実行ファイルを指定できます。
OS別の実行対象は[workflow](../.github/workflows/composition.yml)で確認できます。
GitHub Pagesの配信権限はCIから分離したままです。

### CIの対象と再利用

LinuxとWindowsは別jobとし、それぞれx64／arm64でnative検証を実行します。
`--host-only`は正本の`Lumyte.slnx`から現在のOS用solutionを`artifacts/ci/`へ生成します。
LinuxではWindows Platform・Direct3D12と専用sample／testを除外し、WindowsではLinux Platformを除外します。
Platformのテストprojectもhost用の参照とソースだけをコンパイルします。
共通projectの追加は正本への登録だけで反映されます。OS専用projectを追加した場合は`Build.Ci.cs`の分類も更新してください。
引数を省略した通常のsolution操作は従来どおり全projectを対象にします。

Browserは独立したLinux x64 jobで、nativeのLinux／Windows jobと並列に実行します。
Browser jobはsolution全体をビルドせず、検証するBrowser sampleとその依存だけをpublishします。
共通のsample・package検証はnativeのLinux x64で実行します。
AOTは独立したLinux x64 jobで、対象sampleだけをpublish・実行します。
Windows専用packageとWindow sampleはWindows側で検証します。
FormatはLinux／Windows各x64の独立したjobで、前段のhost用Release成果物を受け取りC#の書式を検査します。
native／Markdown lintはLinuxのFormat jobで一度だけ実行します。
AOT・Browser・4環境のbuildは並列に開始します。native検証とFormatはbuildの成果物を受け取り、すべて成功してからParleyの統合packageを作成します。
環境の動作確認はBuildで行い、CIでは独立したVerify environment／Native smokeを実行しません。
Parleyの4 RID収集と診断galleryのartifact名は共通のままです。

セットアップ後は`dotnet run --no-build --no-restore`でrunnerを再利用します。
`--`より後の同名引数は対象projectの再ビルドを抑制するもので、両方を指定する意味が異なります。
Release成果物をnative検証・配布検証で再利用します。
Browser publishは別checkoutで実行し、nativeと`bin/obj`を共有しません。
書式検査のためだけのDebug全体ビルドは行いません。

CIの依存キャッシュは`.github/actions/cache-dependencies`で管理します。
ツールはOS・architecture・native／browser・固定バージョン定義で分け、BrowserのWasm workloadも専用SDK内に保存します。
NuGetとCargoのダウンロード済みpackageはSDK・依存定義でキーを更新し、同じrunner image・OS・architecture・profile・SDKの以前のキャッシュも利用します。
Rustの保存先はcheckout内の`artifacts/build/tools/`に固定します。
Windowsのmise bootstrap・Mesa・vcpkgダウンロードも別キャッシュで再利用し、セットアップ時のハッシュ検証は維持します。
vcpkgのbinary cacheとParleyのCargo増分ビルドも保存し、Cargoは互換性のあるtool／依存定義の範囲で以前のcacheへfallbackします。
cache hitでもsetupは実行し、manifest・環境設定・OS依存関係を準備します。
projectの`bin/obj`や生成済みNuGetは実行をまたぐcacheには保存しません。

project指定のない`Pack`は`src/`からhost・Parley除外条件に合うprojectを集め、MSBuild一回で順にpackします。
`IsPackable`は各projectのMSBuild評価に従い、復元・コンパイルは再実行しません。

```sh
dotnet run --project build -c Release -- --target CiNative --host-only
dotnet run --project build -c Release --no-build --no-restore -- --target CiDistribution --host-only --no-build --no-restore --skip-parley-package
dotnet run --project build -c Release --no-build --no-restore -- --target TestAot --host-only --no-build --no-restore
dotnet run --project build -c Release --no-build --no-restore -- --target CiBrowser
dotnet run --project build -c Release --no-build --no-restore -- --target CiQuality --host-only
```

### ビルド成果物の共有

4環境の`build` jobがhost用Releaseビルドを行い、`linux`・`windows`と各x64の`format` jobへ成果物を渡します。
`ArchiveCiBuild`は`artifacts/ci/build.tar`を作成します。
対象は`build`・`src`・`tests`・`samples`・`benchmarks`の`bin`／`obj`、
Parleyのnative資産・notice・package資産、Direct3D 12のnative DLLです。
tool cache、Cargo／CMakeの中間出力、ログは含みません。
tarで実行権限とsymlinkを保持し、`build-<runner image>-<architecture>-Release` artifactとして7日間保存します。

[restore-ci-build](../.github/actions/restore-ci-build/action.yml)は同じworkflow runのartifactを展開し、
`VerifyCiBuild`でcheckoutの絶対パス・OS／architecture・configuration・host-only指定と主要出力を確認します。
`obj`やテストfixtureに絶対パスが含まれるため、producerとconsumerは同じrunner label・checkout位置を使います。
その後`Restore --host-only`でNuGet依存を補い、cache miss時にも動作するようにします。
通常のテストではrunner自身と対象projectの両方に`--no-build --no-restore`を指定します。
BrowserとNativeAOTは別構成のため、引き続き独立したjobで必要なprojectだけをpublishします。

LinuxのDXCは`dxc-<architecture>` artifactとして別途1日間保存し、Linuxのtest／format jobでは
setupより先に展開します。DXCのキャッシュはbuild jobで利用しますが、後段への受け渡しは
同じrunのartifactで保証します。キャッシュが削除されてもARM64のDXCを再コンパイルしません。

実行時間を評価するときはrunner待機とstep実行時間を分けます。
PR #103のrun `41230245255`は全体約55分で、Linux ARM64の後段setupが844秒かかりました。
このjobではDXCキャッシュが見つからず、DXCをソースから再ビルドしていました。
同runのbuild jobではキャッシュを復元できていたため、後段でも取得できるとは限りません。
今回のartifact共有はこの再ビルドを除去しますが、短縮量は次のCIで計測する必要があります。

追加の短縮候補は、4環境のbuild matrixをOS別または環境別のjobへ分離し、
各test jobが必要なproducerだけを待つ構成です。現状の`needs: build`は全matrixの完了を待ちます。
runnerの開始待ちも長いため、stepの高速化だけで全体時間が同じ割合で短くなるとは限りません。
Browser／AOTの独立実行と、全検証成功後にpackageを作る条件は維持します。

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
