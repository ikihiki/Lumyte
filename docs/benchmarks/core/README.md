# Coreベンチマークのケース設計

基準コミットは `05d391955a607e7fb948812c9660dc2bed32704d`。本書とケース詳細は提案であり、製品API、ベンチマーク実装、Fallout、CI設定を変更しない。

## 読む順序

1. [共通の測定契約とモード](../../adr/core/CORE-0005-benchmark-catalog-and-modes.md)で、ケースを先に定義し、実行選択を後から決める方針を確認する。
2. 以下の分野別カタログで、処理境界、入力、reset、正当性の確認方法、対応環境を確認する。
3. [既存ケースの統合表](existing-workloads.md)で、新規作成より再利用を優先できる箇所を確認する。
4. ケースの抜けと重複を確認してから、具体ケースのモード・実行頻度・予算を選ぶ。

## 分野別カタログ

- [数値・メモリ・文字編集](numeric-memory-text.md): 時刻と数値、幾何・色、予算とメモリ寿命、Unicodeと編集。
- [Signals・Resources・Settings](signals-resources-settings.md): 依存グラフ、配送、非同期変換、資源の構築と世代・stream、設定のclone・merge・保存。
- [Execution・Composition・状態・Worlds](execution-composition-worlds.md): command、batch、graph、workflow、生成器、状態遷移、Worldの進行。

## 全17プロジェクトの対応

| プロジェクト | 設計の帰属 | 詳細 |
| --- | --- | --- |
| Lumyte.Core | 時刻演算・手動clockと利用側統合。OS待ち時間を算術処理と混ぜない | 数値・メモリ・文字編集 |
| Lumyte.Mathematics | 幾何・補間・色・pixel変換の独立ケース | 数値・メモリ・文字編集 |
| Lumyte.Memory | scope・rent・予算・回収・保持・競合 | 数値・メモリ・文字編集 |
| Lumyte.Memory.DependencyInjection | 構成・解決は準備時または利用側統合。DIだけの件数ノルマは作らない | 数値・メモリ・文字編集 |
| Lumyte.TextEditing | 文書・Unicode境界・transaction・composition。fontやOS IMEは別カテゴリ | 数値・メモリ・文字編集 |
| Lumyte.Signals | 読み出し・Computed・配送・batch・scope・scheduler・async | Signals・Resources・Settings |
| Lumyte.Resources | 構築・再利用・依存・世代・lease・reload・trim・stream | Signals・Resources・Settings |
| Lumyte.Resources.DependencyInjection | 登録・解決をresource利用側へ帰属 | Signals・Resources・Settings |
| Lumyte.Resources.Embedded | 埋め込み資源のlookup・snapshot・read。準備条件と実I/Oを明示 | Signals・Resources・Settings |
| Lumyte.Resources.Execution | resourceとExecutionの統合ケース | Signals・Resources・Settings |
| Lumyte.Settings | clone・overlay・検証・編集・保存。メモリstoreと実diskを分離 | Signals・Resources・Settings |
| Lumyte.Execution | admission・batch・graph・range・workflow・thread・停止と解放 | Execution・Composition・状態・Worlds |
| Lumyte.Execution.Generators | 生成時の初回・増分・無変更・診断。生成コード実行とは分離 | Execution・Composition・状態・Worlds |
| Lumyte.Composition | slot・引数・snapshotの独立または利用側ケース | Execution・Composition・状態・Worlds |
| Lumyte.Composition.Generators | 生成時の初回・増分・無変更・診断 | Execution・Composition・状態・Worlds |
| Lumyte.StateMachines | 定義構築・候補選択・guard・遷移・通知 | Execution・Composition・状態・Worlds |
| Lumyte.Worlds | layer・frame境界・Update・FixedUpdate・Draw配送 | Execution・Composition・状態・Worlds |

## この段階で決めることと残すこと

各ケース群に安定したID、測定対象、入力の代表・境界・負荷、正当性の確認方法とモード適性を与える。単にスクリーニングしにくいという理由で、初回生成、回収、実thread競合、生成器、実I/Oのケースをカタログから削らない。

実装済みの一覧ではなく、意味の異なる経路を分離するための設計である。ケース群と具体的な入力条件の組は異なる単位であり、ケース群数は実行時間の見積もりにならない。全型・全サイズ・全状態の直積は実行条件にしない。

モードはverify・screen・compare・diagnoseの適性を検討する。実行頻度、PRの選択集合、予算、閾値、正式比較の反復数はカタログを確認した後の選択事項とし、詳細文書の例を既定のCI設定にはしない。複数カテゴリに依存するケースは測定主体へ一度だけ帰属させ、他カテゴリからはその結果を参照する。

Core以外のAnimation、Input、Audio、UI、Graphics、Platform、Diagnosticsはこのカタログの対象外である。Coreを利用するUIや描画frameを、Core全体の処理として移動・二重計上しない。
