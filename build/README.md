# 共通ビルド

ビルド・テスト・publish・NuGet pack・検証は、[Fallout](https://github.com/Fallout-build/Fallout)の共通runnerから実行します。
入口は[global.json](../global.json)に対応する.NET SDKです。Falloutのglobal install、PATH上のPython、ビルド用shell scriptは不要です。
compiler・Slang・mise環境の準備は[開発環境](../docs/development-environment.md)を参照してください。
Linuxの初期セットアップも利用可能な.NET 10 SDKを前提にし、JSON読み取りやNative smokeにシステムPythonを使用しません。

CIの直接入口は`Compile`・`Test`・`Quality`・`Distribution`の4種類です。
準備工程は内部のasync methodで順序を保ち、独立targetを増やしません。
開発用・MSBuild用を含む全18 targetの責務と移行表は[TASK-MIGRATION.md](TASK-MIGRATION.md)を参照してください。

## 通常の実行

リポジトリルートから実行します。引数なしの既定targetは`Compile`です。

```sh
dotnet run --project build -c Release -- --target Compile
dotnet run --project build -c Release -- --target Test
dotnet run --project build -c Release -- --target Publish
dotnet run --project build -c Release -- --target Distribution --distribution-output packages
```

`Test`と`Distribution`は必要なrestore・compileから実行します。`Publish`の既定projectはParleyのBrowserサンプルです。
対象やconfigurationも同じ入口で指定できます。

```sh
dotnet run --project build -c Release -- --target Compile --configuration Debug
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Text.Parley.Tests
dotnet run --project build -c Release -- --target Publish --project samples/Lumyte.Graphics.Text.Parley.Browser.Sample --output artifacts/text-browser
dotnet run --project build -c Release -- --target Distribution --distribution-output packages --project src/Graphics/Lumyte.Graphics.Text.Parley
dotnet run --project build -c Release -- --target Test --no-build --no-restore
```

`dotnet run`の`-c Release`はrunner自身の構成、`--`以降の`--configuration`は対象projectの構成です。
`--no-build --no-restore`では、同じconfigurationで作成済みの成果物を使用してください。
Parleyのpackには、後述する4 RID分のnative資産が必要です。
手元のhost以外の資産がない状態で、他のライブラリだけをpackする場合は`--skip-parley-package`を指定します。修正済みwgpu-nativeの全RID資産が未収集の場合は`--skip-wgpu-package`も指定します。これは配布物の選択であり、native実行・機能検証や既定packの欠落資産チェックは省略しません。

```sh
dotnet run --project build -c Release -- --target Distribution --distribution-output packages --skip-parley-package --skip-wgpu-package
```

## 公式構成と責務

[公式の推奨構成](https://github.com/Fallout-build/Fallout/blob/main/docs/website/01-getting-started/02-setup.md)に従います。

```text
.fallout/
  parameters.json             root marker・共通パラメーター
  build.schema.json           生成されるschema
build/
  _build.csproj               実行するconsole project
  Build.cs                    共通CLIとrestore・compile・pack
  Build.Testing.cs             CPU / integration / GPUの排他的選択
  TestLanes.json               test project既定・混在class・実在レーンの分類
  Build.Browser.cs             Browser publish・process検証
  Build.UI.cs                  UIのシナリオカタログと共通検証
  Build.Diagnostics.cs         診断process・UI・gallery
  Build.Samples.cs             sample・AOT・publish
  Build.Quality.cs             format・lint
  Build.Validation.cs          Text・Signals・window等の検証workerとログ
  Build.*.cs                  その他の機能別工程
  Directory.Build.props       親MSBuild設定の自動importを遮断
  Directory.Build.targets
  Targets/                    各projectのMSBuild hook
```

CIとmiseは同じtargetを呼びます。CIにはOS／architecture、権限境界、artifact保存を残し、実行するコマンド列はC#側で管理します。
MSBuild、Cargo、Slang、CMakeは各工程のworkerとして、コンパイルと増分判定を担当します。

`_build.csproj`はsolutionへ登録し、通常のsolution buildでは自身を対象から外します。
MSBuild hookは`Native --native-backend ...`と`Setup --setup-tool wasm-python`だけを呼び、solution全体へ再入しません。
実行中のrunnerを利用できる場合は再利用し、それ以外のhook用出力は呼び出し元projectごとに分けます。
入れ子のleaf実行ではFalloutの状態rootも`artifacts/build/invocations/<id>`へ分け、
`--repository-root`で実際のcheckoutを渡します。これにより`.fallout/temp`のログが競合せず、Cargoとcacheは同じリポジトリを参照できます。

## Target

選択肢と旧入口からの移行は[task option migration](TASK-MIGRATION.md)を参照してください。
レーン・UI feature・benchmark areaはtargetを増やさずオプションで選択します。

| Target | 処理 |
| --- | --- |
| `Restore` / `Compile` / `Test` / `Clean` | 対象projectまたはsolutionの復元・build・test・clean。TestはTRXを保存 |
| `Publish` | 指定projectをpublish。既定はParley Browserサンプル |
| `Distribution --distribution-output packages` | 指定project、または`src/`のpack可能なprojectをNuGet化 |
| `Quality --quality-scope managed` | 復元済みのsolutionへdotnet formatを適用し、変更せず検査 |
| `Quality --quality-scope markdown` / `Format --quality-scope markdown` | Markdownのlint／自動修正 |
| `Quality --quality-scope native` / `Format --quality-scope native` | C/C++の書式検査／自動修正 |
| `Verify --verify-scope environment` / `Verify --verify-scope native` | 共通環境の検証／Slang・CMake・vcpkg・Native NuGet・P/Invoke・software GPUの検証 |
| `Native --native-backend direct3d12` | 選択したAgility SDK・MSVC・CMakeによるDirect3D 12 native DLLのビルド。`--agility-channel Stable/Preview` と復元済み `--agility-sdk-root` を使用 |
| `Setup --setup-tool wasm` / `Test --suite wasm` | wasm-toolsの導入／Graphics・Parley Browserのpublishと実WebGPU検証 |
| `Test --suite ui --feature layout` / `Test --suite ui --feature layout --aot` | core layout全テストとフォント不要Headless配置画面／strict NativeAOT publishと実行 |
| `Test --suite ui --feature controls` / `Test --suite ui --feature controls --aot` | Controlsの通常入力・意味情報と明示フォント付きHeadless設定画面／そのstrict NativeAOT publishと実行。前者は先にsolutionをCompile |
| `Test --suite ui --feature media` | Media・Parley adapterの単体検査と実フォントを渡すHeadless sample（先にsolutionをCompile） |
| `Test --suite text` / `Test --suite rust` | 共通Text・Parley／Rust shimのテスト |
| `Test --suite signals` / `BenchmarkSignals` | Signalsの単体テスト・sample・xUnitによるbenchmark動作確認／BenchmarkDotNet測定結果の保存 |
| `Benchmark --benchmark-area core` / `Benchmark --benchmark-area animation` / `Benchmark --benchmark-area input` | 対象別のカタログ列挙・method選択・BenchmarkDotNet測定 |
| `Benchmark --benchmark-area core --benchmark-evaluate` | [Coreのカテゴリ評価](../benchmarks/Core/README.md)。DI・連携を除外し、分割直列測定と予定／実測ケースの集計を実施 |
| `ComponentScreeningPlan` / `ComponentScreeningRun` | PRの固定SHAから測定対象を選び、同一ハーネスで直列比較・解析・証拠保存。Pythonスクリプトは使用しない |
| `ComponentScreeningNotify` | default branchの信頼済みrunnerで結果リンクをPRへ通知。測定workflowとは権限を分離 |
| `Test --suite browser-contracts` / `Test --suite windowing` | Graphics・Audio・WindowingのJavaScript契約／Linux X11の検証 |
| `Test --suite browser-audio` | Browser Audioをpublishし、専用Python／Playwrightで検証 |
| `Test --suite diagnostics-processes` / `Test --suite diagnostics-ui` | 診断プロセス通信／診断サーバーとWeb UIの検証 |
| `Distribution --distribution-output diagnostic-gallery` | 検証結果から診断galleryを生成 |
| `Distribution` / `Test --suite aot` | CPU・Audio等のsample／NativeAOT publishと実行 |
| `Test --lane gpu --pipeline-validation` / `Distribution --distribution-output window-sample` | Vulkan benchmarkの保存／単一Windowサンプルのpublish |
| `Test --lane cpu` | managed契約。Linux x64のみで実行 |
| `Test --lane integration` | 外部tool・process・native libraryの契約 |
| `Test --lane gpu` | 実backend・pixel/readback。既存opt-in条件を維持 |
| `Test --suite browser` | JavaScript契約・WebGPU・Browser Audio。Wasm toolsの導入を共有 |
| `Distribution` | sample実行・NuGet pack |
| `Quality` | C#・C/C++・Markdownの書式検査 |

X11には`xvfb-run`、Browser検証にはNodeとChromium／Chromeが必要です。`CHROME`で実行ファイルを指定できます。
OS別の実行対象は[workflow](../.github/workflows/composition.yml)で確認できます。
GitHub Pagesの配信権限はCIから分離したままです。

### CIの対象と再利用

CPUは独立したLinux x64 jobのTest --lane cpuで一度だけ実行します。GPUも独立Linux x64 jobのTest --lane gpuへ分離し、cold-cache容量試験とbackend/pixel検証を維持します。
LinuxとWindowsは別jobとし、それぞれx64／arm64でbuildとTest --lane integrationを実行します。Windows／ARMでCPU全suiteは繰り返しません。
分類と混在projectの選択規則は[テスト手順](../tests/README.md#実行レーン)を参照してください。
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
FormatはLinux／Windows各x64の独立したjobで、host用Releaseビルドを準備してC#の書式を検査します。
native／Markdown lintはLinuxのFormat jobで一度だけ実行します。
CPU・GPU・AOT・Format・Browser・nativeは並列に開始し、すべて成功してからParleyの統合packageを作成します。
Browserの既知timeout許可は既存の狭い条件・期限のままとし、レーン全体の失敗を許可しません。
環境の動作確認はBuildで行い、CIでは独立したVerify environment／Native smokeを実行しません。
Parleyの4 RID収集と診断galleryのartifact名は共通のままです。

セットアップ後は`dotnet run --no-build --no-restore`でrunnerを再利用します。
`--`より後の同名引数は対象projectの再ビルドを抑制するもので、両方を指定する意味が異なります。
Release成果物をnative検証・配布検証で再利用します。
Browser publishは別checkoutで実行し、nativeと`bin/obj`を共有しません。
書式検査のためだけのDebug全体ビルドは行いません。

CIの依存キャッシュは`.github/actions/cache-dependencies`で管理します。
ツールはOS・architecture・native／browser・固定バージョン定義で分け、BrowserのWasm workloadも専用SDK内に保存します。
NuGetとCargoのダウンロード済みpackageは依存定義でキーを更新し、同じOS・architecture・profileの以前のキャッシュも利用します。
Rustの保存先はcheckout内の`artifacts/build/tools/`に固定します。
Windowsのmise bootstrap・Mesa・vcpkgダウンロードも別キャッシュで再利用し、セットアップ時のハッシュ検証は維持します。
projectの`bin/obj`や生成済みNuGetはキャッシュせず、毎回検証します。

project指定のない`Distribution --distribution-output packages`は`src/`からhost・Parley除外条件に合うprojectを集め、MSBuild一回で順にpackします。
`IsPackable`は各projectのMSBuild評価に従い、復元・コンパイルは再実行しません。

```sh
dotnet run --project build -c Release -- --target Test --lane integration --host-only
dotnet run --project build -c Release --no-build --no-restore -- --target Distribution --host-only --no-build --no-restore --skip-parley-package --skip-wgpu-package
dotnet run --project build -c Release --no-build --no-restore -- --target Test --suite aot --host-only --no-build --no-restore
dotnet run --project build -c Release --no-build --no-restore -- --target Test --suite browser
dotnet run --project build -c Release --no-build --no-restore -- --target Quality --host-only
```

## ParleyのNative／WASM

| Target | 処理 |
| --- | --- |
| `Native --native-backend parley` | host用のcdylibとRust標準ライブラリのnoticeを生成 |
| `Native --native-backend parley-wasm` | .NETのEmscriptenとABIを合わせたstaticlibを生成 |
| `Setup --setup-tool parley` / `Setup --setup-tool parley-wasm` | native用／WASM用Rust toolchainとrust-srcを準備 |
| `Setup --setup-tool wasm-python` | Emscripten用Pythonを解決し、MSBuildへパスを渡す |

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

`Distribution --distribution-output packages`は全4 RIDのライブラリとnoticeを検証し、一つでも不足すると失敗します。
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
| `--skip-wgpu-package` | 修正済み全RID資産が未収集のとき、Wgpu以外のライブラリをpack |
| `--dotnet-path` | 子プロセスに使うdotnetの明示指定 |
| `--repository-root` | MSBuild leaf workerが独立した状態rootを使う場合の実checkoutパス |
| `--toolchain` | Parley leaf targetのRust toolchainを上書き |
| `--emsdk` / `--cache` / `--node` | .NETのWASM toolchainパス。通常はMSBuildが渡す |
| `--python` / `--python-path-file` | Pythonの明示指定／`Setup --setup-tool wasm-python`の解決結果の出力先 |
| `--cargo` / `--rustc` / `--rustup` | Rust実行ファイルの明示指定 |

nativeのOS linker、Slang、.NET workloadは別途必要です。
WindowsはMSVC／Windows SDK、LinuxはC linkerと開発用ライブラリ、macOSはCommand Line Tools等を使用します。

共通の生成物は`artifacts/`、NuGetは`artifacts/nuget`へ出力します。
Parleyのnative／WASM成果物は`artifacts/text-parley`、Rust・Pythonの専用cacheは`artifacts/build/tools`へ保存します。
Falloutの一時ファイルはrootごとの`.fallout/temp`へ置きます。
MSBuild leafの状態rootは`artifacts/build/invocations`以下です。
globalなPATH、shell profile、GPUドライバー登録は変更しません。

Fallout.Buildは10.4.0で固定し、[packages.lock.json](packages.lock.json)に解決結果を記録します。
Windowsのリダイレクト出力で上流helpが`Console.BufferWidth`に失敗するため、公開初期化hookで実target graphと継承を含む`ParameterAttribute`からhelpを出力します。
console handleや幅を参照せず、`--help`は子toolを起動しません。targetやoptionの別カタログは持ちません。
Rustも[Cargo.lock](../native/lumyte_text_parley/Cargo.lock)に従い`--locked`でbuildします。
Rustupのbootstrapは[RustBootstrap.json](RustBootstrap.json)の公式URLとSHA-256で確認します。
初回の復元・tool取得にはネットワーク接続が必要です。

設計は[ADR-0004](../docs/adr/0004-build-orchestration.md)、文字描画は[GRAPHICS-0024](../docs/adr/graphics/GRAPHICS-0024-text-rendering.md)を参照してください。

## 名前付き色の保守

Mathの通常compile後に、登録済みoffline toolでchecked-in sourceを生成・検証します。
BeforeBuildでMath依存toolを起動する循環はありません。他の検証gateは変更しません。

```sh
dotnet run --project build -c Release -- --target GenerateNamedColors --project src/Core/Lumyte.Mathematics
dotnet run --project build -c Release -- --target VerifyNamedColors --project src/Core/Lumyte.Mathematics
```

値の規則と明示的なreference更新は[保守tool](../tools/colors/Lumyte.NamedColors.Generator/README.md)を参照してください。

C5の検証にはTest --suite ui --feature scroll（core/ControlsとHeadless画面）およびTest --suite ui --feature scroll --aot（strict NativeAOTとnative実行）を使う。

## 修正済みwgpu-native

`Native --native-backend wgpu`は、未送信command bufferのnative解放漏れを補正したABI互換ライブラリを、checksumで固定したソースから作成します。Rust/Pythonは既存のprivate SDK解決を再利用し、通常のWgpu buildとpublishへ修正版をコピーします。

```sh
dotnet run --project build -c Release -- --target Native --native-backend wgpu --output artifacts/wgpu-native
```

ソースpin・必要なnative compiler/libclang・override・全RIDのpack条件は[native手順](../native/wgpu_native/README.md)を参照してください。未収集の他RIDをhostのbinaryで代用しません。修正前のUI測定データは旧baseの記録として保持し、修正後の結果と混ぜません。

タブと折り畳みの検証にはTest --suite ui --feature tabs（通常入力と実編集接続）とTest --suite ui --feature tabs --aot（strict NativeAOTの実行）を使う。

C7の検証にはTest --suite ui --feature collections（項目所有権・選択・Popupと明示fontのHeadless画面）とTest --suite ui --feature collections --aot（strict NativeAOTとnative実行）を使う。

C6の検証にはTest --suite ui --feature numeric（Controls・TextEditing・Automationと実editorのHeadless画面）およびTest --suite ui --feature numeric --aot（strict NativeAOTとnative実行）を使う。sampleには明示的なfont fileを渡す。

メニューと受動tooltipの検証にはTest --suite ui --feature menus（Controls・Windowingの通常入力と公開Headless画面）、
Test --suite ui --feature menus --aot（strict NativeAOTのpublishと実行）を使います。サンプルは明示fontを受け取ります。

分類レーンはartifacts/test-lanes/へ正本Lumyte.slnxを参照する.slnfとproject別VSTest filterを生成し、全選択projectを一度のdotnet testで実行します。
[一括実行の設計と選択規則](TASK-MIGRATION.md#one-sdk-test-invocation-per-classified-lane)を参照してください。

Required-CI timing baselines, cache provenance, Browser case ownership, and the
ten-minute evaluation method: [CI runtime budget](../docs/ci-runtime-budget.md).
