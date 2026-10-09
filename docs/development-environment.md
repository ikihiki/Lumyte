# 開発環境

Debian／Ubuntu 系 Linux（x64／arm64）で、mise を使って .NET SDK、GCC、CMake、Ninja、Slang、Vulkan、lavapipe、vcpkg を準備する。設計方針は [ADR-0003](adr/0003-development-environment.md) を参照する。

Windows x64／ARM64 では PowerShell から同じ mise 設定を使用し、MSVC、Windows SDK、vcpkg、lavapipe を準備する。

## Linux セットアップ

リポジトリルートから実行する。

```bash
bash tools/setup/setup.sh
source tools/setup/activate.sh
mise run verify
```

セットアップは再実行できる。`mise.toml` に .NET SDK、CMake、Ninja、Slang のバージョン・取得元・チェックサムと vcpkg のコミットを定義し、`mise.lock` に従って導入する。GCC、pkg-config、lavapipe などの OS パッケージは APT で更新する。管理者権限または非対話 sudo がなければ、パッケージを `artifacts/dev-env/sysroot/` に展開する。非特権モードの前提は Bash、APT、ディストリビューションの公式鍵、dpkg-deb、curl、Git、GCC／G++ である。

新しいシェルでは毎回 `source tools/setup/activate.sh` を実行する。保存済みファイルは再利用できるが、前のプロセスの環境変数は引き継がれるとは限らない。

有効化後は以下の mise コマンドを使用できる。

```bash
mise run setup                 # OS 依存とツールを含む再セットアップ
mise run setup-native          # vcpkg と lavapipe ICD の準備
mise run setup-wasm            # 固定 workload-set の wasm-tools を導入
mise run test-wasm             # Wasm publish と browser WebGPU の検証
mise run verify                # 開発環境の機能検証
mise run check-native-format   # C/C++ の書式チェック
mise run format-native         # C/C++ の書式を修正
mise run check-markdown        # Markdown の lint
mise run format-markdown       # 自動修正可能な Markdown の違反を修正
mise exec -- dotnet --version  # 固定したツールで実行
mise exec -- slangc -version  # Slang コンパイラーのバージョンを確認
mise ls --current             # 使用するツールのバージョンを確認
```

Slang はシェーダー言語のコンパイラー `shader-slang/slang` の 2026.19 を使用する。Linux x64／arm64 は glibc 2.28 向けの公式アーカイブ、Windows x64／ARM64 は公式 ZIP を取得し、GitHub リリースが公開する SHA-256 digest で検証する。mise がアーカイブ内の `bin/` を PATH に追加するため、有効化後は `slangc` を使用できる。

共通環境変数は `mise.toml` に定義し、OS ごとの有効化スクリプトが mise とコンパイラー環境を準備する。.NET、CMake、Ninja、Slang は HTTP backend を使用し、公式アーカイブを既知のチェックサムで検証する。mise のデータ・設定・キャッシュも `LUMYTE_ENV_ROOT/mise/` に保存し、書き込めないホームディレクトリに依存しない。

インストール先を変更する場合は、セットアップ時と有効化時の両方に同じ `LUMYTE_ENV_ROOT` を設定する。

```bash
export LUMYTE_ENV_ROOT=/path/to/writable/lumyte-env
bash tools/setup/setup.sh
source tools/setup/activate.sh
```

`LUMYTE_SETUP_ROOTLESS=1` を指定すると、sudo があってもローカル展開を使用する。初回セットアップと新しい vcpkg 依存の取得にはインターネット接続が必要になる。

## Windows セットアップ

Windows x64／Windows 11 ARM64 のネイティブな Windows PowerShell 5.1 または PowerShell 7 で、リポジトリルートから実行する。ARM64 では x64 エミュレーションのシェルを使用しない。

```powershell
.\tools\setup\setup.ps1
. .\tools\setup\activate.ps1 -RequireCompiler
mise run verify
```

Git、OS と同じアーキテクチャの MSVC コンパイラー、Windows SDK、VC++ ランタイムが不足する場合は、管理者として起動した PowerShell でセットアップする。既存の Visual Studio／Build Tools は再利用し、ARM64 では ARM64 用のコンポーネントを選ぶ。Git の自動導入には winget が必要。Microsoft インストーラーの署名を検証してから実行し、再起動が要求された場合は再起動後にセットアップを再実行する。組織の PowerShell 実行ポリシーにも従う。セットアップ後の smoke test は管理者権限のないシェルで実行する。Vulkan loader は管理者権限のプロセスでは lavapipe 選択用の環境変数を無視する。

.NET SDK、CMake、Ninja、Slang は mise の Windows x64／ARM64 用ロックに従って導入する。mise 自身も OS と同じアーキテクチャの実行ファイルを使用する。新しいシェルでは `. .\tools\setup\activate.ps1 -RequireCompiler` で MSVC のネイティブ開発環境と mise を有効化する。導入先を変更する場合は、セットアップと有効化の前に `$env:LUMYTE_ENV_ROOT` を同じ書き込み可能なパスに設定する。

Vulkan loader とヘッダーは vcpkg から取得するため、別途 Vulkan SDK を入れる必要はない。ターゲットとホストの triplet は x64 で `x64-windows-static`、ARM64 で `arm64-windows-static` を使用する。Native NuGet はそれぞれ `win-x64`／`win-arm64` に DLL を格納する。

lavapipe は x64 で `pal1000/mesa-dist-win` の MSVC 配布、ARM64 で同プロジェクトが案内する `mmozeiko/build-mesa` の ARM64 配布を使用する。どちらも固定版・SHA-256 で検証し、環境ディレクトリ内に展開する。7zip は固定コミットの vcpkg の検証済みツールを使う。GPU ドライバーの登録は変更しない。追加の HTTPS 接続先は Microsoft の `aka.ms` とそのリダイレクト先、winget の Git 配布先である。

GitHub Actions の Windows Server 2022 x64 でセットアップと smoke test が成功している。既存の MSVC／Windows SDK を再利用する経路を検証した。Microsoft インストーラーによるシステム依存の新規導入は未検証。

## devcontainer

Docker と VS Code の Dev Containers 拡張を用意し、このリポジトリを「Reopen in Container」で開く。

`.devcontainer/Dockerfile` は共通セットアップスクリプトを実行する。ツールは `/opt/lumyte` に保持し、非 root の `lumyte` ユーザーで開発する。mise の shims を PATH に登録する。コンテナ作成後はマウントされたリポジトリの設定を trust してセットアップを再確認し、`mise run verify` を実行する。GPU、X11、Wayland の転送は不要。

セットアップスクリプトや固定バージョンを変更した場合は、コンテナを再ビルドする。

TLS を終端するプロキシ環境では、管理者が提供する CA 証明書を Docker BuildKit の任意の secret `proxy-ca` として渡せる。例: `docker build --secret id=proxy-ca,src=/path/to/proxy-ca.crt -f .devcontainer/Dockerfile .`。証明書はコンテナの信頼ストアに登録する。通常の環境では指定不要で、TLS 検証は常に有効にする。

## Codex クラウド環境

環境設定のセットアップスクリプトでは、チェックアウトのルートに移動して共通スクリプトを呼び出す。チェックアウト位置が `/workspace/Lumyte` の場合は以下を使用する。

```bash
set -eu
cd /workspace/Lumyte
bash tools/setup/setup.sh
source tools/setup/activate.sh
mise run verify
```

タスク開始時には、既存のチェックアウトを使用し、`source tools/setup/activate.sh` でツールを有効化する。クラウドタスクはすでに隔離されているため、ユーザーから明示的に依頼されない限り Git worktree を追加しない。常駐サービスは不要。

必要な主な HTTPS 接続先は、ディストリビューションの APT ミラー、`builds.dotnet.microsoft.com`、`github.com`、`release-assets.githubusercontent.com`、`codeload.github.com`、NuGet の `api.nuget.org` と `globalcdn.nuget.org`。追加する vcpkg port によって取得元が増える場合がある。資格情報はこのセットアップには不要。

## GitHub Actions

`.github/workflows/composition.yml` は push、pull request、手動実行で Linux x64（`ubuntu-24.04`）、Linux aarch64（`ubuntu-24.04-arm`）、Windows x64（`windows-2022`）、Windows aarch64（`windows-11-arm`）を確認する。各ジョブは mise セットアップ、環境検証（`mise run verify`）、`Lumyte.slnx` の Release ビルド、ソリューションに登録された `tests/` 内のテストの順に実行する。先行ステップが失敗した場合は後続のビルド・テストを実行しない。他の構成の検証は継続する。テスト結果は構成ごとに TRX 形式の artifact として保存する。Linux x64 では追加でサンプル、パッケージ、書式も確認する。

GitHub の Windows runner は管理者権限で動くため、システム依存の導入後に一時的な通常ユーザーで smoke test を実行する。`tools/setup/ci-windows-smoke.ps1` がユーザーの作成、検証プロセスの待機、ユーザーの削除を担当する。このスクリプトは GitHub Actions 専用で、通常の開発環境では管理者権限のないシェルから `mise run verify` を使う。

## コードスタイル

ルートの `.editorconfig` と `Directory.Build.props` が共通設定であり、新規 C# プロジェクトにも適用される。StyleCop.Analyzers は file-scoped namespace を扱える `1.2.0-beta.556` に固定し、公開 API の XML ドキュメントも検査する。

開発環境を有効化して、対象プロジェクトの書式確認とビルドを実行する。

```bash
dotnet format whitespace <project.csproj> --verify-no-changes --no-restore
dotnet format style <project.csproj> --verify-no-changes --no-restore --severity warn
dotnet build <project.csproj> -warnaserror
```

`--no-restore` の書式確認は復元済みのプロジェクトを対象とする。修正する場合は `--verify-no-changes` を外す。コンパイラー・アナライザー・NuGet の警告は共通設定でエラーになる。`-warnaserror` は MSBuild タスクの警告もエラーにするために指定する。`mise run verify` でも同じ設定を一時プロジェクトにコピーし、書式確認と警告のエラー化を実行する。C++ の検証用ターゲットにも CMake の `COMPILE_WARNING_AS_ERROR` を設定する。

命名規則は `.editorconfig` の `dotnet_naming_style` と `dotnet_naming_rule` で定義する。`dotnet_diagnostic.IDE1006.severity = error` と共通の `EnforceCodeStyleInBuild` により、命名違反もビルド時にエラーになる。固定 SDK の .NET 10.0.401 で、private フィールドの `_` 不足と async メソッドの `Async` 不足がビルドと `dotnet format style` で検出されることを確認済み。

C/C++ の書式はルートの `.clang-format` に定義する。4 空白、LF、120 桁、波括弧の改行、制御文への波括弧追加、型側のポインター・参照記号を使用し、include の順序は保持する。整形後の差分は確認する。[clang-format の設定仕様](https://clang.llvm.org/docs/ClangFormatStyleOptions.html)を参照する。

```bash
mise run check-native-format   # 変更せずに検査。違反があれば失敗
mise run format-native         # 自動修正
mise exec -- clang-format --version
```

Windows も同じ mise タスクを使用する。Git 管理下と未追跡の C/C++ ソース・ヘッダーを対象とし、Git の無視対象である `artifacts/` や外部依存の生成物は走査しない。`mise run verify` と既存 CI でも書式を検査する。

clang-format 22.1.0 は [cpp-linter の standalone binaries](https://github.com/cpp-linter/clang-tools-static-binaries) の `2026.09.01-5fb8802d` リリースから取得し、GitHub 公開の各 asset の SHA-256 で検証する。mise の版指定は配布リリースの日付 `2026.09.01` であり、clang-format 自体の版とは異なる。Linux／Windows の x64／ARM64 の URL とチェックサムを `mise.toml` と `mise.lock` に固定する。

Markdown は [markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2) 0.23.3 と Node.js 24.21.0 を mise で導入する。設定は `.markdownlint-cli2.jsonc`。既定のルールを有効にし、行長制限（MD013）を無効にする。同名見出し（MD024）は同じ親見出しの範囲で検査し、インライン HTML（MD033）は禁止する。リストは 2 空白でインデントし、コードブロックには言語を指定する。

```bash
mise run check-markdown
mise run format-markdown
```

隠しディレクトリを含む Markdown を検査する。Git の無視対象、`artifacts/`、`.git/`、`node_modules/` は除外する。チェックは違反があれば失敗する。自動修正できない違反は手動で直し、修正後は再度チェックする。リンク先の存在や文章の内容は検査しない。Linux／Windows の `mise run verify` と既存 CI でも lint を実行する。npm のキャッシュは環境ディレクトリの `npm-cache/` に置く。

`mise.lock` が参照する `.mise/locks/` の npm 依存ロックもリポジトリに保持する。devcontainer のセットアップにもこのディレクトリをコピーし、依存パッケージを固定して導入する。

## Native 依存とローカル NuGet

`VCPKG_ROOT` は固定コミットの vcpkg を指す。Native プロジェクトを追加する際は manifest を作り、CMake に以下を指定する。

```bash
cmake -S <native-project> -B artifacts/build/<project> -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
```

非特権モードでは、Vulkan を検索する際に以下も指定する。

```bash
-DVulkan_INCLUDE_DIR="$LUMYTE_VULKAN_INCLUDE_DIR" \
-DVulkan_LIBRARY="$LUMYTE_VULKAN_LIBRARY"
```

ローカル NuGet フィードは `artifacts/nuget/`。ルートの `NuGet.config` に登録している。将来のエンジンパッケージの生成手順は、各プロジェクトの実装時に定義する。

## 検証内容

`mise run verify` は Linux で `verify.sh`、Windows で `verify.ps1` を実行する。最初に C/C++ の書式と Markdown の lint を確認し、生成した検証用プロジェクトで次を確認する。

1. Slang で検証用 compute shader を SPIR-V にコンパイルし、生成物のサイズとマジックナンバーを確認する。
2. vcpkg で zlib を取得・ビルドし、CMake／Ninja で C++ 共有ライブラリを作る。
3. 共有ライブラリを `Lumyte.Setup.Smoke.Native` NuGet に格納し、ローカルフィードから C# プロジェクトへ復元する。
4. C# から P/Invoke で C++ を呼び、zlib の圧縮・復元結果を確認する。
5. lavapipe の CPU デバイスを選び、Vulkan キューにバッファ書き込みを送信し、読み戻した値を検証する。
6. Windows では Direct3D 12 の WARP デバイスを作成する。

検証用 NuGet はエンジンの製品パッケージではない。検証のたびに別のバージョンを生成してキャッシュによる誤判定を避ける。検証プロジェクトは終了時に削除し、ダウンロードキャッシュと生成した NuGet は `artifacts/` に保持する。

lavapipe の ICD は `LUMYTE_LAVAPIPE_ICD` に設定する。検証スクリプト内だけでドライバーを選択し、通常のシェルでは GPU のドライバー選択を変更しない。手動で CPU Vulkan を使う場合は次のように実行する。

```bash
VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" vulkaninfo --summary
```

## バージョンの更新と対象外

配布ツールの更新では `mise.toml` のバージョン・各 OS／CPU 向け取得元とチェックサムを更新する。Slang は `shader-slang/slang` の公式リリースの SHA-256 digest を使用する。.NET は公式リリースメタデータの SHA-512 を使用し、`global.json` も同じ SDK バージョンに更新する。vcpkg は `mise.toml` の `vars.vcpkg_commit` を更新する。mise 自身の更新だけは `tools/setup/mise-bootstrap.json` のバージョンと公式 SHA-256 を更新する。

clang-format の更新では cpp-linter の配布リリース、実行ファイルの clang-format バージョン、4 プラットフォームの URL・SHA-256 を確認し、mise の版指定も配布日付に更新する。書式が変わる場合は `mise run format-native` の差分を確認する。

Markdown ツールの更新では、`mise.toml` の Node.js と `npm:markdownlint-cli2` の版を更新する。markdownlint-cli2 が要求する Node.js の版を確認し、ロックの再生成後に `mise run check-markdown` と `mise run format-markdown` の結果を確認する。

設定の更新後はロックを再生成し、セットアップと検証を行う。

```bash
mise lock --platform linux-x64 --platform linux-arm64 --platform windows-x64 --platform windows-arm64
mise run setup
mise run verify
```

Linux x64 は検証済み。Linux arm64 向けには取得元・チェックサムとロックを用意しているが、実機での実行検証は別途必要。

Windows の Mesa 更新は `mise.toml` の `vars.windows_mesa_version`、x64 用の `vars.windows_mesa_sha256`、ARM64 用の `vars.windows_mesa_arm64_sha256` を更新する。Windows x64 は GitHub Actions の Windows Server 2022 で、Vulkan の読み戻しと Direct3D 12 WARP を含む smoke test を検証済み。Windows ARM64 の検証結果は追加した Windows 11 ARM64 の CI で確認する。Browser の WebAssembly workload は以下の mise タスクで管理する。

## WebAssembly workload と CI

.NET workload は独立したツールではなく、mise が管理する SDK にインストールする追加機能です。`mise.toml` の `vars.dotnet_workload_version` に workload-set のバージョン `10.0.401` を固定し、`mise run setup-wasm` から `dotnet workload install wasm-tools --version ...` を実行します。workload の pack・manifest は mise の SDK インストール先に保存され、NuGet の取得には既存の環境内キャッシュを使います。`mise.lock` は SDK の取得を管理し、workload のバージョンは mise の vars と .NET workload-set が管理します。

```bash
source tools/setup/activate.sh
mise run setup-wasm
CHROME=/usr/bin/google-chrome mise run test-wasm
```

`test-wasm` は `setup-wasm` に依存し、Browser サンプルの trimmed publish と実ブラウザー上の .NET／WebGPU 検証を順に実行します。既にインストール済みの pack は再利用します。ローカルで Chromium の実行ファイル名が `chromium` の場合、`CHROME` の指定は不要です。

既存 CI の Linux x64 ジョブで同じタスクを実行し、出力ログをテスト結果 artifact に保存します。ブラウザーは runner にある Google Chrome と SwiftShader を使用します。Linux arm64／Windows のジョブは通常のソリューションビルド・テストを実行し、Wasm workload は導入しません。Linux x64 の native GPU テストも、共通環境が準備した lavapipe を使用します。

SDK または workload を更新するときは、mise の SDK バージョン・チェックサム、`global.json`、workload-set の互換性を確認し、`vars.dotnet_workload_version` を更新します。CI で workload の導入、Wasm publish、実 WebGPU の実行が成功することを確認してください。
