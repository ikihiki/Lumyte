# ADR-GRAPHICS-0030: Drawing2D の準備・再利用・記録を区別して計測する

- 状態: 採用
- 日付: 2026-11-11
- 基準コミット: `8ea844540cd98d2beb70eebb132e6086be66e662`

## 背景

Drawing2D の開発者は、フレーム準備の遅延が描画命令の変換、バッチ構築、データ配置、キャッシュ不一致のどれに由来するかを切り分けたい。現在の累積 `RetentionStatistics` だけでは、個々の準備呼び出しと対応付けられず、CPU の命令記録と GPU 実行も区別できない。

基準コードの `Renderer2D.PrepareAsync(DrawList2D, ...)` は連続する同型・同一クリップの命令をまとめる。`PrepareRetainedAsync` は最大 16 ページの組ごとにキャッシュを検索し、不一致の組だけ `TransformPages` とバッチ準備を行う。直接描画には独立した計画コンパイラーは存在しないため、架空の planning 段階は設けない。

## 決定

内部 EventSource `Lumyte-Drawing2D-Profiling` に、実在するメソッドと分岐に対応した区間と集計値を追加する。EventSource → EventPipe → dotnet-trace で取得し、準備、キャッシュ再利用、CPU 命令記録、資源返却を同じフレームで比較できるようにする。

### 利用者の変更前・変更後

描画側の `await renderer.PrepareAsync(...)`、`Record`、完了確認後の解放という記述と所有権は変更しない。

| 同じ調査操作 | 変更前 | 変更後（提案） |
| --- | --- | --- |
| 遅い準備呼び出しを調べる | 呼び出し全体を外から計時し、累積統計を差し引く | provider を有効にして取得し、準備区間と再利用・再構築数を読む |
| 記録が速くなった理由を調べる | Record の所要時間だけを比較する | CPU 記録区間と resident な組数を読み、GPU 時間とは分ける |

公開 API・契約の追加、削除、改名はない。新しい sink、session、公開計測 API、InternalsVisibleTo は追加しない。RenderGraph のコンパイルと pass 実行、GPU 計時は対象外とする。

### イベントと相関

イベント ID は `1=StageBegin`、`2=StageEnd`、`3=StageCounter` に固定する。Stage 12 の解放操作は追加の `4=ReleaseOperation`（Level 4、Stages=2）で ProfileRunId、FrameId、対象 SpanId と operation（1=Retire、2=AbortUnsubmitted、3=Dispose、4=ReleaseAfterDeviceRetirement）を発行し、同じ区間に一意に対応付ける。区間は Level 4（Informational）・Stages=2、集計は同レベル・Counters=4 を使う。予約 keyword は Control=1、GPU=8 とし、この provider では発火しない。EventSource の入れ子 `Keywords` クラスとその `const` フィールドは `public` に宣言し、provider 自体は内部に保つ。

イベントは `ProfileRunId(Guid)`、`FrameId(long)`、`SpanId(long)` と `ParentSpanId(long)`、数値 stage ID を持つ。SpanId は provider 内で一意にし、親は同じ provider の有効区間だけを指す。モジュール間は run/frame で結び、他 provider の span を親にしない。root の ParentSpanId は 0 とする。

準備の入口では `Activity.Current` から Parent を順にたどり、custom property `Lumyte.Profiling.RunId` と `Lumyte.Profiling.FrameId` が Guid/long としてそろう最も近い context を読む。該当しなければ Guid.Empty / FrameId=0 の未帰属イベントとして、有効な keyword の計測を続ける。段階ごとの Activity は作らない。

非同期区間は開始時の相関と親をローカルに保持し、`ConfigureAwait(false)` 後にも同じ SpanId で閉じる。返却する prepared owner には run/frame のみを内部保持し、後日の Record・解放はそれを使う別の root span とする。既に終了した Prepare を親にしない。

StageEnd の outcome は `0=成功`、`1=取消`、`2=失敗`、`3=スキップ`。OperationCanceledException は取消、それ以外の例外（cleanup との AggregateException を含む）は失敗とし、既存例外を変換・握り潰さない。開始済み区間は finally 相当で一度だけ閉じる。非選択の経路には空区間を作らない。

### 挿入する区間

stage ID はこの provider 内に固定する。表の型は `src/Graphics/Lumyte.Graphics.Drawing2D/` 配下にある。

| ID・段階 | 開始するメソッド・位置 | 終了位置・意味 |
| --- | --- | --- |
| 1 PrepareDirect | `Execution/Renderer2D.cs` の `PrepareAsync(DrawList2D, ...)` 入口、検証前 | PreparedDraw2D の構築と `_preparing` 解除後。失敗時の既存バッチ破棄を含む |
| 2 PrepareRetained | `Execution/Renderer2D.Retained.cs` の `PrepareRetainedAsync` 入口、検証前 | source.Revision 再検証・frame 構築と `_preparing` 解除後。失敗時の ReleaseFrame を含む |
| 3 TransformPages | retained miss 分岐の `TransformPages` 呼び出し直前 | 返却直後。親 transform と clip の反映、命令リスト再構築だけ |
| 4 BuildBatches | 直接経路の while 直前、または retained miss の transformed.Commands の while 直前 | ループ終了／例外。TakeBatch、renderer 選択、await するカスタム renderer も含む |
| 5 RectangleRuns | `Rendering/PreparedRectangleBatch2D.PrepareAsync` の resources 選択前 | 返却／例外 cleanup 後。Eligible による連続 run 分割、最大 1024 個制限、一般経路への委譲を含む |
| 6 CompilePrograms | `Rendering/PreparedGpuBatch2D.PrepareAsync` の Add ループ直前 | ループ直後／例外。DrawingProgram2D.Compile、有限値検査、命令・データの構築を含む |
| 7 PackUpload | 同型の `UploadAsync` 入口 | 共有 buffer 割当、MapAsync、CopyFrom、Unmap、参照登録の終了／例外。CPU 壁時計であり転送完了ではない |
| 8 IndividualPrepare | `Rendering/PreparedIndividualGpuBatch2D.PrepareAsync` 入口 | AddAsync ループと失敗 cleanup の終了。命令構築と配置が交互なので分離せず、経路全体を測る |
| 9 RectanglePackUpload | `PreparedRectangleBatch2D.SolidBatch.PrepareAsync` 入口 | Fill、buffer rent、MapAsync、CopyFrom、Unmap、引数登録、配列返却・失敗 cleanup 後 |
| 10 Record | `Execution/PreparedDraw2D.Record` 入口 | clear、barrier、全 batch.Record の終了／例外。CPU のコマンド発行時間 |
| 11 DisposeDirect | `PreparedDraw2D.Dispose` 入口 | 逆順破棄と未解放リスト更新、AggregateException の送出まで |
| 12 ReleaseRetained | `PreparedDrawingFrame2D` の Retire / AbortUnsubmitted / Dispose / ReleaseAfterDeviceRetirement の各入口 | 前提検証、Release と cache.ReleaseFrame の終了／例外。操作種別は Retire=1、AbortUnsubmitted=2、Dispose=3、ReleaseAfterDeviceRetirement=4 の数値タグ |

`PreparedDrawingFrame2D.Record` は保存した相関を内側の PreparedDraw2D.Record に適用し、検証も含む一つの stage 10 とする。内側の重複区間は発火しない。AttachSubmission は区間を追加せず、提出・完了待ちの時間を Record に足さない。

stage 6 の後で共有配置不可と判定した場合、生成済みの仕事は成功区間として残し、その後に stage 8 を記録する。非 SPIR-V または命令数 2 未満なら stage 6/7 を作らず stage 8 に進む。キャッシュ hit は stage 3/4/5–9 を実行しないため、それらの 0 時間や skip イベントを捏造しない。

入口の引数・状態不正は失敗、事前取消は取消として外側区間を閉じる。空の入力は成功（仕事数 0）。既に解放済みの Dispose と ReleaseAfterDeviceRetirement の早期 return は skip とし、禁止された再 Record・Retire は既存どおり失敗にする。

### 集計値と単位

StageCounter は数値 counter ID と long 値を持つ段階終端のスナップショットとする。ループ内で数だけ加算し、命令・矩形・ページごとのイベントは出さない。同じ段階が複数回呼ばれる場合の区間イベントは、ページ組またはバッチ単位とする（命令ごとに Add/Compile を囲まない）。

| counter ID・名前 | 計測箇所・単位・解釈 |
| --- | --- |
| 1 InputCommands | stage 1 で list.Commands.Count、命令数の実数 |
| 2 PageGroups | stage 2 で実際に lookup を試した最大 16 ページの組数、組 |
| 3 ReusedRuns | stage 2 で Take が非 null だった回数、組。カウンタ取得用に追加 lookup はしない |
| 4 CompiledRuns | stage 2 で CountCompilation に到達した回数、組。途中失敗した構築を成功数に含めない |
| 5 PreparedBatches | stage 4 で追加済み batch 数、batch。描画 call 数とは異なる |
| 6 Instructions | stage 6/8 で生成済み program.Instructions.Count の合計、要素 |
| 7 DataVectors | stage 6/8 の program.Data.Count、stage 9 の Fill 戻り値の合計、Vector4 要素 |
| 8 HostCopiedBytes | stage 7/8/9 で成功した CopyFrom の実ペイロード byte 数。命令は要素数×ShaderElementStrideInBytes、Vector4 は要素数×16 |
| 9 RectangleRuns | stage 5 で追加済み fast/general run 数、run。resources 不在の全体委譲は 0 |
| 10 RectangleInstances | stage 9 の commands.Length、instance |
| 11 ActiveFrames | stage 2/12 終端の Statistics.ActiveFrames、slot 数。busy と quarantine を含む |
| 12 CachedAssets | 同じ snapshot の CachedAssets、idle の組数 |
| 13 CachedBytesEstimate | 同じ snapshot の CachedBytes、byte。EstimatedBytes による保守的推定で、GPU 常駐量や managed allocation 実測ではない |
| 14 ResidentRuns | stage 10 で実際に Record に進んだ Asset.Resident=true の組数、組 |

1–10、14 は呼び出し局所の集計、11–13 は renderer 全体のその時点の値である。累積 CompiledRuns/ReusedRuns の差し引きでは他フレームの操作と混ざり得るため、3/4 は実分岐で数える。失敗時にはその時点までの値を残し、未取得値は省略する。

キャッシュ hit は完了済み・idle な asset の排他的取得を意味し、単に同じページ内容だったこととは異なる。custom renderer は再利用可能とは仮定しない。ResidentRuns は upload を省略する経路の根拠だが、HostCopiedBytes も含め GPU 実行済み byte 数を表さない。

### ソース根拠

上記の全ソース参照は基準 SHA に固定する。準備・Record・解放の根拠は表のパスとメンバー、キャッシュの根拠は `Execution/RetainedDrawingCache2D.cs` の Take / ReleaseFrame / Statistics、統計単位は `Execution/DrawingRetentionStatistics2D.cs` である。

基準 SHA の `tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/RetainedRenderingCacheTests.cs` は完了後の residency、キー不一致、cleanup retry を、`RetainedRenderingLifetimeTests.cs` は Record / Retire の前提とカスタム renderer の再準備を確認している。`PreparedDrawCleanupTests.cs` は失敗した batch のみを再破棄する境界を確認している。

## 検討した代替案

- PrepareAsync 全体と累積統計だけを取る: 追加負担は小さいが、再利用と変換・配置のどこが重いか判断できない。
- 命令ごとの Activity とイベントを取る: 細粒度で追えるが、大量の矩形や描画命令で観測自体の負担が増える。区間はバッチ境界、命令数は集計にする。
- 全経路を compile と upload に強制分割する: 見かけはそろうが、individual 経路の AddAsync は両者を交互に実行する。ソースを再構成せず経路固有の区間を残す。

## 結果と影響

開発者は、retained hit と再構築、CPU の配置と命令記録をフレーム単位で区別できる。計測無効時は keyword/level の有効判定より先に文字列化・payload 配列・計測専用走査をせず、公開 API と資源寿命を維持する。区間は包含関係を持ち、単純加算をフレーム全体時間としない。

### 検証方針

- provider metadata の event ID、level、keyword と、Begin/End の一対一対応を EventPipe 取得で検査する。途中の await、取消、検証例外、renderer 例外、cleanup 例外でも欠落させない。
- cold → 完了・command disposal → Retire → 同一入力の順で、初回は CompiledRuns、次回は ReusedRuns と ResidentRuns が増え、再利用部分の stage 3–9 が存在しないことを確認する。
- transform、clip、target サイズ・format・PixelScale の変更と、busy/quarantine、cache budget 0、custom renderer で再利用数を誤認しないことを確認する。
- 空入力、クリップ・型の切替、1024/1025 矩形、fast/general 混在、非 SPIR-V、共有 buffer 上限による fallback で区間と集計を照合する。
- Record 失敗後の AttachSubmission/Retire 拒否、未提出 abort、Dispose の skip、cleanup 再試行、device retirement で所有権と既存例外を維持する。
- 別 provider の span を親にせず、Current が context を持たない場合は Parent の最も近い一致を使うことを確認する。Activity のない単独 benchmark でも Guid.Empty / FrameId=0 で発火し、後日の Record/解放は準備時の run/frame を保持することを確認する。
- disabled/enabled の負担を別に比較する。性能改善量は保証せず、CPU 区間と host copy 集計から GPU 所要時間を推定しない。
