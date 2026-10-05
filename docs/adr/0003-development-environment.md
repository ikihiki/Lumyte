# ADR-0003: mise による共通の Linux／Windows 開発環境セットアップ

- 状態: 採用
- 日付: 2026-10-05

## 背景

devcontainer と Codex のクラウド環境で C#、C++、Vulkan の開発に同じセットアップ手順を使う。クラウド環境には GPU や管理者権限がない場合がある。共通セットアップを mise で管理する。バージョン、環境変数、実行タスクを devcontainer と Codex で共有し、シェルスクリプトごとにツールの管理方法を持たない構成にする。

## 決定

Debian／Ubuntu 系 Linux の x64／arm64 と Windows x64 を対象にする。devcontainer と Codex のクラウド環境は Linux、Windows ネイティブ環境は PowerShell を入口とし、同じ mise 設定を共有する。devcontainer は Debian 13 を使用する。

`mise.toml` に .NET SDK、CMake、Ninja のバージョンと各 CPU 向け取得元・チェックサム、vcpkg のコミット、セットアップと検証のタスクを定義する。`mise.lock` に Linux x64／arm64 と Windows x64 の解決済み取得元を保存し、通常のインストールは `mise install --locked` で行う。

.NET、CMake、Ninja は mise の HTTP backend で管理する。公式配布アーカイブを使い、既知の SHA-512／SHA-256 を明示する。.NET の既存の公式 SHA-512 検証を維持するため、検証方法が異なるインストーラースクリプトへ切り替えない。`global.json` は .NET 自身の SDK 選択にも必要なので保持し、mise の指定との一致を検証する。

GCC、pkg-config、Vulkan loader、ヘッダー、lavapipe と各ランタイム依存はディストリビューションに依存するため、APT で導入する。管理者権限がない場合のローカル展開方式と署名・ハッシュ検証は維持する。

vcpkg は Git リポジトリと bootstrap が必要なので、mise タスクで準備する。コミット指定は `mise.toml` に一元化し、vcpkg の依存取得時の検証を維持する。OS パッケージの具体的なバージョンは mise が管理するものではない。

Windows では Microsoft の署名を検証したインストーラーで MSVC Build Tools、Windows SDK、VC++ ランタイムを準備する。Git がなければ winget で導入する。システム依存が不足する場合だけ管理者権限を要求する。Vulkan loader とヘッダーは vcpkg、lavapipe は第三者配布 `pal1000/mesa-dist-win` の固定版 MSVC アーカイブを SHA-256 検証して導入する。Mesa は環境ディレクトリ内に保持し、システムのドライバー登録を変更しない。

### コマンドと責務

| 入口 | 役割 |
| --- | --- |
| `bash tools/setup/setup.sh` | OS 依存導入、固定版 mise の bootstrap、設定の trust、ロックに従うツール導入、Native 環境準備 |
| `./tools/setup/setup.ps1` | Windows のシステム依存、mise、Native 環境を準備する |
| `. ./tools/setup/activate.ps1 -RequireCompiler` | Windows の mise と MSVC 開発環境を有効にする |
| `source tools/setup/activate.sh` | mise と共通環境変数を有効にする |
| `mise run setup` | 共通セットアップを再実行する |
| `mise run setup-native` | 固定コミットの vcpkg と lavapipe ICD を準備する |
| `mise run verify` | C#、C++、vcpkg、Native NuGet／PInvoke、Vulkan キューと読み戻しを検証する |
| `mise exec -- <command>` | 固定したツールでコマンドを実行する |

mise 自身を管理する bootstrap のバージョンと公式 SHA-256 だけは `tools/setup/mise-bootstrap.json` に記録する。生成物は `LUMYTE_ENV_ROOT` 以下に保持し、標準設定では `artifacts/dev-env/`、devcontainer では `/opt/lumyte` とする。

devcontainer のイメージビルドと Codex のセットアップは共通スクリプトを呼ぶ。devcontainer の作成後は実際のマウント先の mise 設定を trust し、検証する。新しいシェルでは共通の有効化スクリプトを使用する。常駐サービスは不要。

### 検証

`mise run verify`（Linux は `verify.sh`、Windows は `verify.ps1`）で以下を確認する。

- .NET と C++ のビルドと実行。
- vcpkg による依存ライブラリのビルドと CMake からの利用。
- lavapipe で Vulkan デバイスを作成し、キューへコマンドを送信して結果を読み戻す。
- Windows では Direct3D 12 の WARP デバイス作成。
- Native ライブラリをローカル NuGet パッケージにして C# から復元し、P/Invoke で呼び出す。

検証用のプロジェクトは生成物として扱い、エンジンの公開 API や製品パッケージを定義するものではない。

## 検討した代替案

### バージョン指定だけを mise に置き、導入は従来のスクリプトに残す

管理が二重になるため、配布済みツールのインストールと PATH 選択も mise に任せる。

### GCC と lavapipe も配布バイナリとして mise に登録する

OS のヘッダーや共有ライブラリとの依存を独自に管理する必要があるため、これらは署名済みディストリビューションパッケージで導入する。

## 結果と影響

- mise の同じ設定とタスクをLinux と Windows の環境で使用できる。
- 配布ツールの更新では `mise.toml` のバージョン・チェックサムと `mise.lock` を更新する。.NET は `global.json` も更新する。
- OS パッケージはディストリビューションの更新を取り込むため、完全なビット単位の再現性は保証しない。Windows 用スクリプトは構文検査済みだが、Windows 実機でのセットアップ・DirectX 実行は未検証。Browser の実行環境は別途整備する。
- 従来の生成物は削除せず保持するが、有効なツールは mise のインストール先から選択する。

## 参考資料

- [ADR の書き方と運用](0001-adr-writing-policy.md)
- [リポジトリのフォルダ構成](0002-repository-layout.md)
