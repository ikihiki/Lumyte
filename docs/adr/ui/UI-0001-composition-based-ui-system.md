# ADR-UI-0001: Compositionで構築する保持型UIシステム

- 状態: 提案
- 日付: 2026-10-26
- 更新: 2026-10-27

## 背景

既存のComposition、Input、Windowing、Drawing2Dの上に、ゲーム内画面とツールのためのUIを構築する。構築構文、親子関係、レイアウト、入力、GPU実行をそれぞれの責務へ分け、実装を段階的にレビューできる必要がある。

本ADRは全体の依存方向を扱う。各実装PRの契約・API・受け入れ条件はUI-0002〜0008で定義する。全て設計提案であり、コードや環境検証の完了を意味しない。UI-0001は未取り込みの提案なので、以前のAPI案を詳細ADRへ整理し、置換済み扱いにはしない。

## 決定

### 構築と依存方向

`src/UI/Lumyte.UI`に.NET 10のmanagedライブラリを置く。assembly・名前空間・NuGet名は`Lumyte.UI`とし、実行時の直接依存はComposition、Input.Abstractions、Graphics.Drawing2Dに限定する。GeneratorはAnalyzer参照する。UIからPlatform、Windowing管理、InputSystem、RenderGraph、Resources、Animation、DI、Diagnosticsへ直接依存しない。

[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)の生成ファクトリ、with、子インデクサー、名前付きスロットを使う。UIは保持型Widgetツリーと値を所有し、毎frameの再生成・自動reconciliationは行わない。CompositionへUI保存領域や寿命管理を追加しない。

```text
Compositionの生成ファクトリ → UIツリー → 計測・配置・入力・描画記録
アプリのWindow/Input統合 ────────────→ UiSurface
UiSurface → DrawingCanvas2D → 既存renderer → GPU実行・提示
```

初期対象はPanel、Stack、Rectangle、単一内容と背景スロットを持つButton、clip、pointer、キー操作である。Text・Imageコントロール、Grid、scroll、仮想化、テーマ、binding、編集・IME、アクセシビリティは別途設計する。現在mainに存在する[Parley文字組みと共通Text](../graphics/GRAPHICS-0024-text-rendering.md)は将来のTextコントロールの接続先とし、UI本体へ文字組みエンジンを組み込まない。

### 実装PRと対応ADR

実装PRの番号は実装順の識別であり、GitHubの採番ではない。全体方針のPRと、各範囲のADRを置く7本のdraft PRに分ける。draftは設計だけを含み、対応する実装・テスト・READMEを追加してから実装PRとしてreadyにする。各draftは直前のブランチをbaseとし、先行ADRの契約を参照できるようにする。具体的なPRへのリンクは全体PRの説明で管理する。

| 実装PR | 対応ADR | 追加する機能 | 実装の前提 |
| --- | --- | --- | --- |
| 1 | UI-0002 | ツリー、所有権、Composition、surfaceの寿命 | UI-0001 |
| 2 | UI-0003 | 計測・配置、寸法、Stack、無効化 | PR 1 |
| 3 | UI-0004 | 矩形、描画記録、clip、直接描画サンプル | PR 2 |
| 4 | UI-0005 | hit test、capture、ButtonのClick | PR 3 |
| 5 | UI-0006 | focus、Tab、キー押下、入力競合 | PR 4 |
| 6 | UI-0007 | Window/Input接続、Hosted、DPI、環境検証 | PR 3・5 |
| 7 | UI-0008 | 親RenderGraph、Resources、device世代の接続 | PR 3・6 |

描画とhit testは同じBounds・座標・clip規則を使うため、PR 3で幾何走査を確定し、PR 4はそれを前提にする。全公開APIを先にstubで追加せず、動作が完成するPRで段階的に公開する。

### 共通の契約

ツリーは参照同一性で状態を保持し、単一親・単一surface・非循環とする。接続中のツリーはsurface作成スレッドで扱う。layout・描画中の変更と再入を拒否し、Click中の通常の変更は許可する。GPU資源とWindowはUIが所有しない。

UI座標は論理pixelである。viewportは論理サイズだけを受け取り、DPI変換はホストがCanvasへ一度だけ適用し、Drawing2DのPixelScaleは1とする。以前の案の`SetViewport(size, scale)`は、未使用のscaleをUIへ保存しない`SetViewport(logicalSize)`へ具体化する。非一様scale、Hostedの原点・clipの合成はUI-0007で定義する。

各詳細ADRのAPI diffはmain `18ae09656616ced27876c758490ddb1a954b4706`を比較元とし、そのPRで追加する部分を示す。同revisionにLumyte.UIは存在しない。先行PRのAPIは相対リンクで参照する。テスト名・配置は実装の目標であり、現時点で存在するという記述ではない。

## 検討した代替案

- 全機能を一つのADR・実装PRへまとめると、親子関係からGPU接続までレビュー範囲が広がるため、主要な判断ごとに分割する。
- 即時モードUIはCompositionの毎回新規生成に加えて状態照合が必要になるため、初期は保持型とする。
- UIがWindow、入力pump、GPU実行を所有すると既存のホスト構成と寿命管理が重複するため、接続を利用側へ置く。

## 結果と影響

構築から実入力・GPU描画まで段階的に検証できる。APIの仕様元を各ADRへ集約する一方、下位の契約を変える場合は依存するADRも整合させる必要がある。

## 検証方針

各実装PRでその範囲のCPUテスト、必要なGPU・実環境検証、利用例を追加する。依存方向、実際のComposition Generatorによる構築、失敗後の状態、寿命を確認する。文書だけの段階では命名、APIの整合性、相対リンクとMarkdownを確認する。
