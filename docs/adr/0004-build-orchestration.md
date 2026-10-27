# ADR-0004: Falloutによるリポジトリ全体のビルド工程の統一

- 状態: 採用
- 日付: 2026-10-27

## 背景

Lumyteのビルドには.NET、Rust、Slang、Native依存の検証が関わる。ビルド、テスト、WASM publish、NativeAOT、NuGet packのコマンドがCI・mise・機能ごとのスクリプトへ分散すると、ローカルとCIの工程や対応環境を揃えにくい。Parleyだけに設けたFalloutの入口をリポジトリ全体へ広げる。

入口として確実に使えるものは.NET SDKであり、PATH上のPythonやFalloutのグローバルインストールを前提にしない。開発ツールの取得・選択とOS依存の準備には、引き続き[ADR-0003](0003-development-environment.md)のmise環境を利用する。

## 決定

### 公式のフォルダ構成

Falloutの公式setupが生成する配置に従う。

```text
Lumyte/
├── .fallout/
│   ├── parameters.json
│   └── build.schema.json
├── build/
│   ├── _build.csproj
│   ├── Build.cs
│   ├── Build.*.cs
│   ├── Directory.Build.props
│   ├── Directory.Build.targets
│   └── Targets/
└── Lumyte.slnx
```

`.fallout/`をroot marker、`parameters.json`を共通パラメーターの入口とする。`build/_build.csproj`を通常の.NET console projectとして実行し、`Build.cs`と責務別のpartial classへ工程を置く。`build/Directory.Build.props`と`Directory.Build.targets`は親ディレクトリの自動importを遮断し、runnerのビルドがアプリ側のhookへ再入することを防ぐ。

`_build.csproj`はsolutionへ登録するが、通常のsolution buildではビルド対象から外す。runner自身の再ビルドや、同じbin／objへの並列書き込みを避ける。MSBuildから呼ぶleaf targetは、実行中のrunnerまたは呼び出し元ごとに分けたrunner出力を利用する。

入れ子のleaf実行はFalloutの状態rootも呼び出しごとに分離し、実際のcheckoutを別のパラメーターで渡す。`.fallout/temp`のログ競合を防ぎつつ、Cargoやtoolchainのcacheは同じリポジトリを参照する。

### 一つの入口と工程の責務

通常の入口を次の形に統一する。

```sh
dotnet run --project build -c Release -- --target Compile
```

CIとmiseは同じFallout targetを呼ぶ薄い入口にする。ビルド順序と共通パラメーターはC#側で管理し、CIには実行するOS／architecture、権限境界、artifactの保存と配信を残す。公式のbootstrap scriptやlocal tool manifestを追加する場合も、同じbuild projectへ接続する。

適用対象はsolutionのrestore・build・test、sampleの実行、WASM・NativeAOTのpublish、NuGet pack、書式・lint、開発環境のNative smoke、Browser・音声・診断の検証とする。既存の検証を共通化のために削除しない。

MSBuildとCargoは各言語のコンパイルと増分判定を担う。Slangのソース加工・WGSL／SPIR-V生成・埋め込みもMSBuildのworkerとして維持し、Falloutのsolution buildから起動する。通常のC#プロジェクトを直接ビルドした場合にも必要なParleyのnative成果物を作れるよう、MSBuild hookは共通runnerのleaf targetへ接続する。leaf targetからsolution全体のビルドへ戻らない。

セットアップ用shell／PowerShellはOS依存の導入、有効化、Windowsの非特権プロセス起動を担当する。ソフトウェアのビルド工程を別実装として保持しない。GitHub Pagesの配信権限は、PRのビルド・テスト権限から分離したままにする。

### toolchain、Python、生成物

FalloutはNuGet参照として固定し、lock fileに解決結果を記録する。Rustとその依存もtoolchain指定と`Cargo.lock`で管理する。取得するRustupやPythonのarchiveは、公式配布元と固定したchecksumを照合する。

Emscriptenや既存の検証スクリプトがPythonを必要とする場合、明示指定・SDK同梱runtime・ビルド用の専用runtimeから解決する。PATH上のPythonを必須にせず、.NETで取得・照合・展開できるようにする。専用toolのcacheは`artifacts/build/tools`へ置き、アプリのruntimeへ追加しない。

外部プロセスには引数を配列で渡す。既存の環境を引き継ぎ、toolchainや個別検証に必要な環境だけをそのプロセスへ追加する。非ゼロ終了は工程の失敗として伝え、後続の配布工程を進めない。globalなPATH、shell profile、GPUドライバー登録は変更しない。

OSのcompiler／linker、Slang、.NETのwasm-tools等は、それぞれの環境・工程の前提として管理する。入口を.NETに統一することと、OS依存のcompilerを不要にすることは同一視しない。
wasm-toolsは固定SDKと同じworkload-setから準備し、暗黙に最新のworkloadへ切り替えない。

具体的なtarget・パラメーター、取得元・version、実行手順は[buildの手順書](../../build/README.md)と実装を正本にする。

### 検証

リポジトリの標準solution buildと、分離した出力先の双方を確認する。特に、solutionへ直接登録しないテスト用helperの構成と出力先を揃え、build後の`--no-build --no-restore`でもテスト資産を利用できることを確認する。

NativeはLinux／Windowsのx64／arm64、Browserは実際の.NET WASM publishとWebGPU実行で確認する。WASMのコンパイル成功だけを実行検証の代わりにしない。取得が必要な初回の経路とcacheの再利用、空白・非ASCII文字を含むパス、システムPythonのない環境も対象にする。実施結果は手順書・CI・PRへ記録し、未検証の環境を検証済みとして扱わない。

## 検討した代替案

| 選択肢 | 評価 |
| --- | --- |
| 機能ごとのFallout runner | 小さい範囲で導入しやすいが、repo全体のtarget・取得処理・パラメーターが重複する |
| CIとmiseへコマンド列を保持 | 既存の構成を保てるが、ローカル実行とCIで工程が分かれ、publishやpackの追加漏れが起こりやすい |
| Pythonで全体を統合 | 記述は短いが、常設interpreterを前提にできない。必要なPythonは.NETの入口から専用runtimeを準備する |
| 独自の.NET CLI | .NETだけを入口にできるが、target graphとパラメーター処理にはFalloutを使用する |

## 結果と影響

ローカルとCIが同じtargetを実行し、複数言語・WASM・配布・検証の工程を一箇所で追える。Falloutの公式構成により、build project、設定、生成物と実装コードの置き場所も明確になる。

Fallout、私有toolchainの取得、各workerとの接続が継続保守の対象になる。入れ子のビルドでの循環と出力先の衝突を防ぐ必要がある。OSごとのセットアップ・権限差と、ネットワークが必要な初回取得は引き続き存在する。

## 参考資料

- [Fallout Build Setup](https://github.com/Fallout-build/Fallout/blob/main/docs/website/01-getting-started/02-setup.md)
- [Fallout Execution](https://github.com/Fallout-build/Fallout/blob/main/docs/website/01-getting-started/03-execution.md)
- [ADR-0002: リポジトリのフォルダ構成](0002-repository-layout.md)
- [ADR-0003: 共通開発環境](0003-development-environment.md)
- [GRAPHICS-0024: 文字組み・カラーフォント・距離場描画](graphics/GRAPHICS-0024-text-rendering.md)
