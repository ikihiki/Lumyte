# ADR-UI-0001: Compositionで構築する保持型UIシステム

- 状態: 提案
- 日付: 2026-10-26
- 更新: 2026-10-29

Compositionの所有content・delegate引数・値adapter・関数factoryの生成拡張はPR #98でmainへ統合済みである。UIスタックはその契約を利用し、汎用の契約・Generator・テストを各UI PRへ重複して含めない。詳細は[Composition ADR](../composition/COMPOSITION-0005-function-factories.md)を参照する。

Signals本体（同期グラフ・非同期変換・性能改善）はPR #99でmainへ統合済みである。UI側の購読・値引数・ページ寿命の接続だけを実装PR 15で扱う。UIスタックは統合後のmainを基点とし、#71のbaseはmainを維持する。

## 背景

既存のComposition、Input、Windowing、Drawing2Dの上に、ゲーム内画面とツールのためのUIを構築する。構築構文、親子関係、レイアウト、入力、GPU実行をそれぞれの責務へ分け、実装を段階的にレビューできる必要がある。

本ADRは全体の依存方向を扱う。各実装PRの契約・API・受け入れ条件はUI-0002〜0012、およびWindow managerのUI-0014・IME候補表示のUI-0015で定義する。全て設計提案であり、コードや環境検証の完了を意味しない。

## 決定

### 構築と依存方向

`src/UI/Lumyte.UI`に.NET 10のmanagedライブラリを置く。assembly・名前空間・NuGet名は`Lumyte.UI`とし、実行時の直接依存はComposition、Input.Abstractions、Graphics.Drawing2Dに限定する。GeneratorはAnalyzer参照する。UI本体からPlatform、Windowing管理、InputSystem、RenderGraph、Resources、Animation、DI、Diagnosticsへ直接依存しない。任意の統合層がWindow managerを注入して利用し、UiSurfaceやWidgetに具象Windowの生成・管理を組み込まない。

[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)の生成ファクトリ、with、子インデクサー、名前付きスロットを使う。UIは表示中の要素と状態を保持し、構築関数の再実行で得たWidget宣言を既存の表示要素と照合する。依存する状態の変更時に再構築を行い、追加・削除・並べ替えを反映する。再構築は要求されたときに行い、毎frameの描画命令生成とは独立させる。CompositionへUI保存領域や寿命管理を追加しない。通常内容・背景はWidgetChildrenへそれぞれ一回だけ初期化する。readonlyストレージへの生成規則は[COMPOSITION-0004](../composition/COMPOSITION-0004-owned-content-collections.md)、所有権と動的UIへの影響は[UI-0002](UI-0002-widget-tree-and-composition.md)を正本とする。

イベントコールバックは`Button(onClick: () => Save())`のように生成ファクトリの名前付きパラメーターで渡す。UI側はnullableなdelegateプロパティとして保持し、省略・nullはハンドラーなし、代入は置換とする。public eventとComposeActionによる追加購読はUIのイベント設定APIとして提供しない。直接delegate型を生成するためのComposition契約の追加は実装PR 4の担当範囲とする。

```text
Compositionの生成ファクトリ → UIツリー → 制約駆動layout・入力・描画記録
アプリのWindow/Input統合 ────────────→ UiSurface
UiSurface → DrawingCanvas2D → 既存renderer → GPU実行・提示
```

初期対象はPanel、Stack、Rectangle、単一内容と背景スロットを持つButton、clip、pointer、キー操作である。Image・一般のTextコントロール、Grid、一般scroll、仮想化、テーマ、binding、アクセシビリティは別途設計する。編集・IMEは実装PR 11の任意TextBoxで追加し、IME候補の独自描画には同PRのImeCandidateViewを使う。[Parley文字組みと共通Text](../graphics/GRAPHICS-0024-text-rendering.md)は将来のTextコントロールの接続先とし、UI本体へ文字組みエンジンを組み込まない。

### 実装PRと対応ADR

実装PRの番号は実装順の識別であり、GitHubの採番ではない。全体方針のPRと、各範囲のADRを置く11本のdraft PRに分ける。draftは設計だけを含み、対応する実装・テスト・READMEを追加してから実装PRとしてreadyにする。各draftは直前のブランチをbaseとし、先行ADRの契約を参照できるようにする。具体的なPRへのリンクは全体PRの説明で管理する。

| 実装PR | 対応ADR | 追加する機能 | 実装の前提 |
| --- | --- | --- | --- |
| 1 | UI-0002 | ツリー、所有権、Composition、再構築と内部状態保持、surfaceの寿命 | UI-0001 |
| 2 | UI-0003 | 一方向layout、min/max制約、寸法、Stack、無効化 | PR 1 |
| 3 | UI-0004 | 矩形、描画記録、clip、直接描画サンプル | PR 2 |
| 4 | UI-0005・COMPOSITION-0002 | hit test、capture、callbackパラメーターと生成対応 | PR 3 |
| 5 | UI-0006 | focus、Tab、キー押下、入力競合 | PR 4 |
| 6 | UI-0007・INPUT-0007 | 取得元Window付きInput、source順序、Window/Input接続、Hosted、DPI | PR 3・5 |
| 7 | UI-0008 | 親RenderGraph、Resources、device世代の接続 | PR 3・6 |
| 8 | UI-0009 | Locator、自動待機、通常入力による操作、状態検証 | PR 5・6。積み上げ先はPR 7 |
| 9 | UI-0010 | Diagnostics経由の操作、Job、操作権lease、結果取得 | PR 8 |
| 10 | UI-0011・UI-0014 | Window manager注入、独立Hosted fallback、Popup・入力scope・診断操作権 | PR 6〜9 |
| 11 | UI-0012・UI-0015 | TextBox・編集文書、TSF/Browser IME、任意の候補描画・候補操作、Fill | PR 6〜10・GRAPHICS-0024 |

描画とhit testは同じBounds・座標・clip規則を使うため、PR 3で幾何走査を確定し、PR 4はそれを前提にする。全公開APIを先にstubで追加せず、動作が完成するPRで段階的に公開する。

再構築と内部状態保持は最初の実装PRから提供する。型・key・子slotによる照合と状態引き継ぎをUI-0002で実装し、後続のlayout・入力・Automation・TextBoxはその基盤へ接続する。Signalによる自動再実行は別途追加する。

### 共通の契約

以下は全体の要約であり、詳細は対応ADRを正本とする。後続ADRは共通契約を参照し、その範囲で追加・変更する条件を記載する。

ツリーは参照同一性を保つ単一親・単一surface・非循環の構造とする。所有権、同時操作の責任、処理順序、破棄はUI-0002で定義する。UI本体はWindowとGPU資源を所有せず、ホストと任意の統合層がその寿命を管理する。UiSurfaceとWidgetは作成threadへ固定せず、利用者が明示的に行う並行操作の同期は利用者が管理する。通常hostは処理順序とthread依存serviceへの内部配送を管理し、ページ側へthread検査や手動dispatcherを要求しない。

レイアウトはFlutter型の「制約を子へ渡す→子がサイズを返す→親が位置を決める」方式とする。一回の子layout、制約、公開Size/Bounds、拡張境界はUI-0003で定義する。描画とhit testは共通の幾何を使う。

UI座標とviewportは論理pixelで扱う。DPI変換はホストのCanvasへ集約し、PixelScale、Hosted原点・clip、入力座標の契約はUI-0007で定義する。取得時に確定したInputWindowIdをINPUT-0007で保持し、後から決まるUI上の配送先Windowと分ける。

任意のLumyte.UI.Automationは、snapshotに基づく検索・待機と通常入力による操作を提供する。C#の同一プロセスAPI、host、失敗・取消はUI-0009で定義する。

別プロセスからの操作は既存Diagnosticsへ接続する任意adapterで提供する。Job、操作権、連番、結果取得はUI-0010で定義し、UI本体・UI.Automationから通信を参照しない。

任意のLumyte.UI.WindowingはIUiWindowManagerを受け取り、通常画面の登録とPopupを扱う。[UI-0014](UI-0014-injectable-window-manager.md)で注入契約、独立HostedUiWindowManager、Nativeとのfallback合成を定義する。Popupの内容・入力scope・寿命・Automation/Diagnostics連携はUI-0011で定義し、WindowSystemへの接続は専用adapterに限定する。

任意のUI.TextEditingとPlatform adapterがIMEと編集を提供する。共通文書、Windows TSF、Browser textarea、focus・入力順と候補位置は[UI-0012](UI-0012-ime-and-text-editing.md)で定義する。候補一覧の独自描画と候補専用操作は[UI-0015](UI-0015-custom-ime-candidate-presentation.md)で定義し、Browserや未対応IMEではSystem表示へ戻す。

初期詳細ADRのAPI diffはmain `18ae09656616ced27876c758490ddb1a954b4706`を比較元とし、そのPRで追加する部分を示す。同revisionにLumyte.UIは存在しない。Window/Diagnostics/IME接続の比較元revisionは各ADRに記載する。先行PRのAPIは相対リンクで参照する。配置と検証項目は実装時の目標とする。

## 検討した代替案

- 全機能を一つのADR・実装PRへまとめると、親子関係からGPU接続までレビュー範囲が広がるため、主要な判断ごとに分割する。
- 即時モードUIはCompositionの毎回新規生成に加えて状態照合が必要になるため、初期は保持型とする。
- UI本体がWindow、入力pump、GPU実行を所有すると既存ホストの寿命管理と重複するため、接続は任意の統合層に置き、Window managerは利用側から注入する。

## 結果と影響

構築から実入力・GPU描画まで段階的に検証できる。APIの仕様元を各ADRへ集約する一方、下位の契約を変える場合は依存するADRも整合させる必要がある。

## 検証方針

各実装PRでその範囲のCPUテスト、必要なGPU・実環境検証、利用例を追加する。依存方向、実際のComposition Generatorによる構築、失敗後の状態、寿命を確認する。文書だけの段階では命名、APIの整合性、相対リンクとMarkdownを確認する。

## スタック全体の性能レビューへの対応

2026-10-31、PR #71の全体レビュー（旧スタックcommit `07c49200`）を受け、以下を担当PRのADR・実装・回帰テストへ反映する。旧測定値と今回の変更前後比較は環境が異なるため、直接比較しない。

| 対象 | 更新境界と所有責任 | 担当PR |
| --- | --- | --- |
| ツリー走査・再構築 | 子初期化で祖先のStructureRevisionと走査cacheを失効。RebuildPlanが照合結果と旧新の走査対象を保持する | #77 |
| Window操作履歴 | 進行中の対応表だけをAdvanceで処理。結果はWindow解放まで保持し、任意のIUiWindowRequestLifetimeで終端結果を早期解放できる | #86 |
| 描画と入力の前後関係 | Fallbackは内側managerの提示済みHitTestを使い、公開Window IDへ変換する | #86 |
| IME座標 | session・文書・layout・原点・clip・scaleの変更時だけ公開。Parleyの区間索引で範囲検索を行う | #87 |
| 静止した標準host | rootと構造revisionが同じ間はEditorを再検証しない。chromeのgeometry要求は一turnへ集約し、自身の結果を解放する | #89 |
| GPU待機 | AcquireからPresentまでをsubmitゲート内で行い、GPU完了・readback・退役をゲート外で待つ。MaximumFramesInFlightで未完了frameを制限する | #89 |
| Signal更新 | 変更のない構造のrecipeを再利用し、走査・照合の重複確保を削減する。接続済み宣言のsetterは更新しない | #93 |

論理commit、最終提示、GPU退役、DI解放の区別を維持する。再構築全体の粒度を変更する部分木境界は別の設計判断であり、今回の走査cache・recipe再利用だけで部分木更新を実装したとは扱わない。無変更時、長い文書、長いWindow寿命、および一部だけ変わる画面を継続的な性能検証対象とする。

## 状態と解放の所有境界

同じ意味の状態を複数のboolや別々のfieldで保持しない。更新の判断は次の所有者へ集約する。

| 対象 | 所有者と不変条件 | 担当PR |
| --- | --- | --- |
| 予約した宣言 | 再構築候補がrootを所有する。Signal導入後は候補の購読も所有し、公開時にだけsurfaceへ移す。適用中に予約された次の候補は次回境界まで残す | #77・#93 |
| 照合と再利用 | RebuildPlanが旧新ツリーと照合結果を持ち、状態準備・identity保持・幾何再利用を実行する。公開後の入力継続と取消通知は入力引継ぎへ分離する | #77・#80・#81・#84・#93 |
| Windowの前後順 | 共通のlayerと提示順を描画とhit testに使う。FallbackでIDを変換しても内側の提示順を維持する | #86・#89 |
| Popupの終了 | 公開状態のClosing/Closed/Failedから終了判定を導く。取消callbackの前にClosingを公開する | #86 |
| ページ交換 | 交換tokenは準備・静止・commit・解放/破棄の段階を持つ。Viewが自分の清掃進捗を所有し、成功済み手順を再実行しない | #90 |

論理commitは新しい入力と宣言の公開、最終提示は演出の終了、GPU退役は旧資源を参照する処理の完了、DI解放はページの所有scopeの返却を意味する。これらは一つの完了flagへまとめない。Viewの清掃再試行と、Navigationが所有するページRetire・DI scope解放も別の進捗として扱う。
