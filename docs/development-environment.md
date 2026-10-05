# 開発環境

Debian／Ubuntu 系 Linux（x64／arm64）で、mise を使って .NET SDK、GCC、CMake、Ninja、Vulkan、lavapipe、vcpkg を準備する。設計方針は [ADR-0003](adr/0003-development-environment.md) を参照する。

Windows x64 では PowerShell から同じ mise 設定を使用し、MSVC、Windows SDK、vcpkg、lavapipe を準備する。

## Linux セットアップ

リポジトリルートから実行する。

```bash
bash tools/setup/setup.sh
source tools/setup/activate.sh
mise run verify
```

セットアップは再実行できる。`mise.toml` に .NET SDK、CMake、Ninja のバージョン・取得元・チェックサムと vcpkg のコミットを定義し、`mise.lock` に従って導入する。GCC、pkg-config、lavapipe などの OS パッケージは APT で更新する。管理者権限または非対話 sudo がなければ、パッケージを `artifacts/dev-env/sysroot/` に展開する。非特権モードの前提は Bash、APT、ディストリビューションの公式鍵、dpkg-deb、curl、Git、GCC／G++ である。

新しいシェルでは毎回 `source tools/setup/activate.sh` を実行する。保存済みファイルは再利用できるが、前のプロセスの環境変数は引き継がれるとは限らない。

有効化後は以下の mise コマンドを使用できる。

```bash
mise run setup                 # OS 依存とツールを含む再セットアップ
mise run setup-native          # vcpkg と lavapipe ICD の準備
mise run verify                # 開発環境の機能検証
mise exec -- dotnet --version  # 固定したツールで実行
mise ls --current             # 使用するツールのバージョンを確認
```

共通環境変数は `mise.toml` に定義し、OS ごとの有効化スクリプトが mise とコンパイラー環境を準備する。.NET、CMake、Ninja は HTTP backend を使用し、公式アーカイブを既知のチェックサムで検証する。mise のデータ・設定・キャッシュも `LUMYTE_ENV_ROOT/mise/` に保存し、書き込めないホームディレクトリに依存しない。

インストール先を変更する場合は、セットアップ時と有効化時の両方に同じ `LUMYTE_ENV_ROOT` を設定する。

```bash
export LUMYTE_ENV_ROOT=/path/to/writable/lumyte-env
bash tools/setup/setup.sh
source tools/setup/activate.sh
```

`LUMYTE_SETUP_ROOTLESS=1` を指定すると、sudo があってもローカル展開を使用する。初回セットアップと新しい vcpkg 依存の取得にはインターネット接続が必要になる。

## Windows セットアップ

Windows x64 の Windows PowerShell 5.1 または PowerShell 7 で、リポジトリルートから実行する。

```powershell
.\tools\setup\setup.ps1
. .\tools\setup\activate.ps1 -RequireCompiler
mise run verify
```

Git、MSVC の x64 コンパイラー、Windows SDK、VC++ ランタイムが不足する場合は、管理者として起動した PowerShell でセットアップする。既存の Visual Studio／Build Tools は再利用する。Git の自動導入には winget が必要。Microsoft インストーラーの署名を検証してから実行し、再起動が要求された場合は再起動後にセットアップを再実行する。組織の PowerShell 実行ポリシーにも従う。セットアップ後の smoke test は管理者権限のないシェルで実行する。Vulkan loader は管理者権限のプロセスでは lavapipe 選択用の環境変数を無視する。

.NET SDK、CMake、Ninja は mise の Windows x64 用ロックに従って導入する。新しいシェルでは `. .\tools\setup\activate.ps1 -RequireCompiler` で MSVC の x64 開発環境と mise を有効化する。導入先を変更する場合は、セットアップと有効化の前に `$env:LUMYTE_ENV_ROOT` を同じ書き込み可能なパスに設定する。

Vulkan loader とヘッダーは vcpkg から取得するため、別途 Vulkan SDK を入れる必要はない。lavapipe は `pal1000/mesa-dist-win` の MSVC 配布を固定版・SHA-256 で検証し、環境ディレクトリ内に展開する。7zip は固定コミットの vcpkg の検証済みツールを使う。GPU ドライバーの登録は変更しない。追加の HTTPS 接続先は Microsoft の `aka.ms` とそのリダイレクト先、winget の Git 配布先である。

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

`.github/workflows/setup-smoke.yml` は push、pull request、手動実行で Ubuntu 24.04 と Windows Server 2022 のセットアップから `mise run verify` までを確認する。両 OS の結果を個別に表示し、一方の失敗で他方の検証を中止しない。[成功した実行結果](https://github.com/ikihiki/Lumyte/actions/runs/37357250382)では、両 OS のセットアップと smoke test が完了した。

GitHub の Windows runner は管理者権限で動くため、システム依存の導入後に一時的な通常ユーザーで smoke test を実行する。`tools/setup/ci-windows-smoke.ps1` がユーザーの作成、検証プロセスの待機、ユーザーの削除を担当する。このスクリプトは GitHub Actions 専用で、通常の開発環境では管理者権限のないシェルから `mise run verify` を使う。

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

`mise run verify` は Linux で `verify.sh`、Windows で `verify.ps1` を実行し、生成した検証用プロジェクトで次を確認する。

1. vcpkg で zlib を取得・ビルドし、CMake／Ninja で C++ 共有ライブラリを作る。
2. 共有ライブラリを `Lumyte.Setup.Smoke.Native` NuGet に格納し、ローカルフィードから C# プロジェクトへ復元する。
3. C# から P/Invoke で C++ を呼び、zlib の圧縮・復元結果を確認する。
4. lavapipe の CPU デバイスを選び、Vulkan キューにバッファ書き込みを送信し、読み戻した値を検証する。
5. Windows では Direct3D 12 の WARP デバイスを作成する。

検証用 NuGet はエンジンの製品パッケージではない。検証のたびに別のバージョンを生成してキャッシュによる誤判定を避ける。検証プロジェクトは終了時に削除し、ダウンロードキャッシュと生成した NuGet は `artifacts/` に保持する。

lavapipe の ICD は `LUMYTE_LAVAPIPE_ICD` に設定する。検証スクリプト内だけでドライバーを選択し、通常のシェルでは GPU のドライバー選択を変更しない。手動で CPU Vulkan を使う場合は次のように実行する。

```bash
VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" vulkaninfo --summary
```

## バージョンの更新と対象外

配布ツールの更新では `mise.toml` のバージョン・各 OS／CPU 向け取得元とチェックサムを更新する。.NET は公式リリースメタデータの SHA-512 を使用し、`global.json` も同じ SDK バージョンに更新する。vcpkg は `mise.toml` の `vars.vcpkg_commit` を更新する。mise 自身の更新だけは `tools/setup/mise-bootstrap.json` のバージョンと公式 SHA-256 を更新する。

設定の更新後はロックを再生成し、セットアップと検証を行う。

```bash
mise lock --platform linux-x64 --platform linux-arm64 --platform windows-x64
mise run setup
mise run verify
```

検証済みの対象は Linux x64。arm64 向けには取得元・チェックサムとロックを用意しているが、arm64 実機での実行検証は別途必要。

Windows の Mesa 更新は `mise.toml` の `vars.windows_mesa_version` と `vars.windows_mesa_sha256` を更新する。Windows x64 は GitHub Actions の Windows Server 2022 で、Vulkan の読み戻しと Direct3D 12 WARP を含む smoke test を検証済み。Browser の WebAssembly workload と WebGPU の実行環境は別途整備する。
