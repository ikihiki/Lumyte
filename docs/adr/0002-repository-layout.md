# ADR-0002: リポジトリのフォルダ構成

- 状態: 採用
- 日付: 2026-10-05

## 背景

Lumyte は C# を主言語とするゲームエンジンであり、DirectX と Vulkan のバックエンドには補助的に C++ を使用する。対象環境は Windows、Linux、Browser、描画 API は DirectX、Vulkan、WebGPU とする。

C# 側は C++ のネイティブライブラリをローカル NuGet パッケージ経由で参照する。設計判断は ADR に記録してから実装する。

今後の機能追加に備え、関連するプロジェクトを探しやすくするとともに、言語の違いで同じ機能の実装が離れない構成が必要である。

## 決定

`src/` を機能カテゴリで一段だけ分類し、その下に正式なプロジェクト名のフォルダを配置する。`managed/` と `native/` による言語別の分割は行わない。

各プロジェクトのフォルダ名はプロジェクト名および NuGet パッケージ名に揃える。C++ プロジェクトには `.Native` 接尾辞を付け、対応する C# プロジェクトと同じカテゴリに配置する。

以下を目標構成とする。プロジェクトやフォルダは実装が必要になった時点で追加し、この ADR のためだけに空のプロジェクトを作成しない。

構成図は配置ルールを示すためのものであり、個別の ADR や文書ファイルは列挙しない。

```text
Lumyte/
├── .devcontainer/                  # 共通セットアップを使う開発コンテナ
├── docs/
│   └── adr/
│       ├── input/                  # 入力システム
│       ├── core/
│       ├── engine/
│       ├── graphics/
│       └── platform/
├── src/
│   ├── Core/
│   │   └── Lumyte.Core/
│   ├── Engine/
│   │   └── Lumyte.Engine/
│   ├── Graphics/
│   │   ├── Lumyte.Graphics.Abstractions/
│   │   ├── Lumyte.Graphics/
│   │   ├── Lumyte.Graphics.DirectX/
│   │   ├── Lumyte.Graphics.DirectX.Native/
│   │   ├── Lumyte.Graphics.Vulkan/
│   │   ├── Lumyte.Graphics.Vulkan.Native/
│   │   └── Lumyte.Graphics.WebGPU/
│   └── Platform/
│       ├── Lumyte.Platform/
│       ├── Lumyte.Platform.Windows/
│       ├── Lumyte.Platform.Linux/
│       └── Lumyte.Platform.Browser/
├── tests/
├── samples/
├── tools/
│   └── setup/                      # 共通セットアップ・有効化・検証
├── packaging/
│   └── nuget/
├── artifacts/
│   └── nuget/
├── CMakeLists.txt
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── mise.toml
├── mise.lock
├── NuGet.config
└── Lumyte.slnx
```

### 各ディレクトリの役割

| ディレクトリ | 役割 |
| --- | --- |
| `docs/adr/` | リポジトリ全体の運用・構成の ADR。直下でプレフィックスなしの連番を採番する |
| `docs/adr/<category>/` | カテゴリごとの設計判断、採用理由、影響の記録。カテゴリ内で独立して採番する |
| `.devcontainer/` | 共通セットアップを呼び出す開発コンテナ設定 |
| `src/Core/` | 基本型と基盤機能 |
| `src/Engine/` | エンジン機能の統合 |
| `src/Graphics/` | 描画の共通契約と描画 API ごとの実装 |
| `src/Platform/` | ウィンドウ、入力などの共通契約と環境ごとの実装 |
| `tests/` | C# の単体テスト・統合テスト。必要に応じてカテゴリとプロジェクト単位で整理する |
| `samples/` | 利用例と最小起動・描画サンプル |
| `tools/` | ビルドやパッケージ生成の補助ツール |
| `tools/setup/` | 開発環境の共通セットアップ、有効化、検証 |
| `packaging/nuget/` | 必要な共通パッケージ定義や MSBuild 統合ファイル |
| `artifacts/` | ビルド生成物。導入時に Git 管理対象外とする |
| `artifacts/nuget/` | 生成したパッケージを配置するローカル NuGet フィード |

カテゴリは配置を整理するためのものであり、フォルダ階層だけで名前空間や依存関係を決めない。新しい機能カテゴリも同じ一段の分類ルールで追加する。

描画バックエンドが実装する共通インターフェースと関連型は `Lumyte.Graphics.Abstractions` に配置する。デバイスの共通契約と依存境界は [グラフィックデバイスの設計](graphics/GRAPHICS-0001-graphics-device.md) に従う。バックエンド固有の API と使い方は各バックエンドプロジェクトの README に記載する。

### ADR のカテゴリ配置

リポジトリ全体の運用・構成に関する ADR は `docs/adr/NNNN-short-description.md` に配置し、タイトルは `ADR-NNNN` とする。機能の設計判断は `docs/adr/<category>/CATEGORY-NNNN-short-description.md` に配置する。採番・命名・変更履歴の詳細は [ADR-0001: ADR の書き方と運用](0001-adr-writing-policy.md) に従う。直下と各カテゴリはそれぞれ `0001` から独立して採番し、別カテゴリの進行中の作業によって番号が衝突しない構成とする。同じ採番範囲内の重複は番号予約で防ぐ。

入力システムの ADR は `docs/adr/input/` に配置し、識別子を `INPUT-0001`、`INPUT-0002` のように採番する。ADR の分類は設計判断の責務を表し、入力実装をどの `src/` カテゴリに配置するかは入力システムの設計 ADR で決定する。

構成図のカテゴリディレクトリは目標構成であり、最初の ADR が必要になった時点で作成する。

### Native プロジェクト

Native プロジェクトの内部は以下の構成とする。

```text
Lumyte.Graphics.DirectX.Native/
├── include/          # 公開ヘッダー
├── src/              # C++ 実装
├── tests/            # C++ テスト
└── CMakeLists.txt
```

`Lumyte.Graphics.Vulkan.Native` も同じ構成を使用する。C# は各プロジェクトの `.csproj`、C++ は各プロジェクトの `CMakeLists.txt` でビルドを管理する。ルートの `CMakeLists.txt` は Native プロジェクトのビルドをまとめる。

| C# プロジェクト | ローカル NuGet で参照する Native パッケージ |
| --- | --- |
| `Lumyte.Graphics.DirectX` | `Lumyte.Graphics.DirectX.Native` |
| `Lumyte.Graphics.Vulkan` | `Lumyte.Graphics.Vulkan.Native` |

C# 側のバインディングは対応する描画バックエンドに配置する。共通の `Lumyte.Native` プロジェクトは設けない。Browser の WebGPU 実装は `Lumyte.Graphics.WebGPU` に配置する。

## 検討した代替案

### `src/` 直下へのフラット配置

パスが短く、少数のプロジェクトでは一覧性が高い。一方、描画、プラットフォーム、将来の Audio や Physics などが増えると一覧が長くなるため、カテゴリ別の配置を採用する。

### `managed/` と `native/` による言語別配置

言語ごとのビルド対象を見分けやすいが、同じバックエンドの C# と C++ が離れる。Lumyte では機能単位での見通しを優先する。

### バックエンドごとの深い階層

`Graphics/DirectX/Native/` のような配置は関連性を表現できるが、パスが深くなり、フォルダ名とパッケージ名の対応も弱くなる。カテゴリの下に正式なプロジェクト名を並べる。

## 結果と影響

- 同じ機能の C# と C++ を同じカテゴリで管理できる。
- ADR をカテゴリ別に探せるようになり、別カテゴリでの並行採番が衝突しない。
- フォルダ名からプロジェクトと NuGet パッケージを特定できる。
- 機能追加時にはカテゴリの選定が必要になる。複数のカテゴリにまたがる機能は責務を整理して配置する。
- ビルドスクリプトとソリューションではカテゴリを含むパスを扱う必要がある。

## 別途決定する事項

本 ADR は配置と命名を定める。以下は個別の ADR で決定する。

- C ABI、P/Invoke、メモリ所有権、エラー伝達などの相互運用方式。
- Native パッケージの RID、バージョニング、生成・復元手順。
- DirectX のバージョンと、環境ごとの描画バックエンドの対応範囲。
- Browser における C# と WebGPU の連携方式。
- 各プロジェクトの依存方向と、最初に実装する起動・描画経路。
