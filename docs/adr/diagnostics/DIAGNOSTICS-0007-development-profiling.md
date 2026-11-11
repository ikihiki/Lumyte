# ADR-DIAGNOSTICS-0007: 本体開発の性能計測を標準 .NET トレースで行う

- 状態: 提案
- 日付: 2026-11-11
- 基準コミット: `3e50e2315743e23f6e5faf7ffdf393ac0a144a2d`

## 背景

Lumyte 本体の開発者は、重いフレームの処理段階と作業量を、ランタイムのスタックサンプルや GC 情報と一緒に調べたい。調査ごとの計測パッチと専用集計器では通常実行との比較が難しく、独自の収集・保存基盤まで持つと .NET の診断機能と責務が重複する。

基準コードの `tools/benchmarks/ui-device/DetailedDiagnostics.cs` は任意パッチの callback から詳細値を集計する一方、同じファイルの `DeviceBenchmarkEvents` は既に `EventSource` でフレーム marker を発行する。`tools/benchmarks/ui-device-trace/Program.cs` は標準トレースを読み取る解析器の入口である。RenderGraph の `src/Diagnostics/Lumyte.Diagnostics.RenderGraph/RenderGraphDiagnostics.cs` は既存の概要集計と要求時の Activity を提供する。

## 決定

本体の処理区間・フレーム・作業量を `EventSource` で発行し、EventPipe と `dotnet-trace` によってランタイムイベントと同じ `.nettrace` へ収集する。Lumyte が追加するのは処理の意味と明示的な相関情報であり、収集バッファ、独自 sink、専用セッション API、保存形式、ビューアは新設しない。

対象は本体開発用の計測である。既存の `System.Diagnostics.Metrics` による概要値と `ActivitySource` による論理的な trace は維持するが、細粒度の全フレーム計測を Activity へ置き換えない。アプリ作者向けの安定した計測 API、UI 要素への帰属、リモート診断操作の追加は対象外とする。

### 利用者の変更前・変更後

同じ「UI 更新から描画までの遅さを調べる」操作を比較する。変更前は `tools/benchmarks/ui-device/detail-instrumentation.patch` を適用して専用ハーネスを build し、`DetailedDiagnostics` の集計とランタイムの trace を照合する。通常アプリには同じ計測点がない。

```sh
git apply tools/benchmarks/ui-device/detail-instrumentation.patch
dotnet build tools/benchmarks/ui-device -c Release
```

変更後は製品の計測点を有効にするためのパッチを適用せず、Release build のアプリまたは既存ベンチマークへ標準ツールを接続する。以下の provider は本 ADR で提案する名前であり、基準コードに実装済みの名前ではない。`12345` は調査対象の PID に置き換える。ツールは別途導入済みとする。

```sh
dotnet-trace collect --process-id 12345 \
  --profile gc-verbose \
  --providers "Microsoft-DotNETCore-SampleProfiler,Lumyte-UI-Application-Profiling:0xF:4,Lumyte-UI-Profiling:0xF:4,Lumyte-Drawing2D-Profiling:0xF:4,Lumyte-RenderGraph-Profiling:0xF:4" \
  --output lumyte.nettrace
```

1. `dotnet-trace ps` で対象を選び、必要な provider と keyword だけを有効にして収集を開始する。起動直後も必要なら標準の `collect -- <実行コマンド>` でアプリを起動する。
2. ウォームアップ後に調査する操作やシナリオを実行する。既存ベンチマークではウォームアップと測定の marker で対象区間を分ける。
3. Enter または `--duration` によって標準の収集セッションを止め、`.nettrace` を保存する。フレーム数指定はベンチマークが実行する仕事量の指定であり、独自の収集セッション機能にはしない。
4. PerfView のイベント表示などで Lumyte の区間とランタイム情報を調べる。自動比較が必要な場合は既存の TraceEvent 解析経路を再利用し、対象フレームの分布を抽出する。
5. 改善候補は同じ入力で比較し、性能差の採否は詳細トレースを無効にしたベンチマークでも確認する。

接続可能な .NET ランタイム、診断ポートへのアクセス、対象と同じ権限など、標準ツールの前提に従う。ブラウザー、診断無効のランタイム、NativeAOT 等で同じ取得方法が使えるとは保証せず、対応可否を環境ごとに明示する。

### 標準機能の役割分担

- `EventSource`: フレーム境界、処理区間、作業量、取得可能な GPU 結果を固定スキーマのイベントとして発行する。
- EventPipe と `dotnet-trace`: provider の有効化、ランタイム情報との収集、バッファリング、保存、収集開始・停止を担当する。
- `System.Diagnostics.Metrics` と `dotnet-counters`: 通常調査用の件数・時間合計・最大値などの概要を扱う。個々のフレームの p95 を合計と最大値から推定しない。
- `ActivitySource`: 既存の論理的な処理追跡を継続する。フレーム計測のために毎区間の Activity を生成しない。

[診断通信の「決定」](DIAGNOSTICS-0001-diagnostics-transport.md#決定)の Meter・ActivitySource・ILogger と既存の有界収集・通信は維持する。新しい EventSource の詳細イベントを既存診断の通信キューへ複製しない。Metrics を直接 `dotnet-counters` で見る経路と、既存診断 collector が受ける概要値は別の購読経路として扱う。

`.nettrace` を一次資料とし、独自の生データ形式を正本にしない。スタック向けの Speedscope 変換だけで独自イベントもすべて保存・表示できるとは扱わない。Perfetto 用変換や専用 UI は取得の前提にせず、必要な場合にイベントの対応を検証して選択する。

### Provider と計測負荷

provider は各責務の assembly が所有する。UI.Application は `Lumyte-UI-Application-Profiling`、UI は `Lumyte-UI-Profiling`、Drawing2D は `Lumyte-Drawing2D-Profiling`、RenderGraph は `Lumyte-RenderGraph-Profiling` を使う。同じ provider 名を複数 assembly から定義しない。共通 sink 用 assembly と計測のための新しい `InternalsVisibleTo` は追加しない。

keyword はフレーム・測定 marker が `0x1`、CPU 区間が `0x2`、作業量が `0x4`、GPU 結果が `0x8` とし、通常は Informational level で発行する。収集例の `0xF` は全種類を選ぶ。フレーム単位の解析ではホストの `0x1` も必ず購読し、段階イベントだけの取得から完全なフレーム分布を作らない。イベント ID・payload の単位と意味は固定し、互換性のない変更には Event version を付ける。

各計測点は `IsEnabled(level, keywords)` を時計取得や payload 作成より前に確認する。未購読時は計測用の時計取得・相関 ID 生成・文字列整形・割当・GPU query を行わない。既存処理や別の診断購読が元から行う計時と ID 生成まで除去するという保証ではない。

有効時も固定名、数値 ID、型付き payload を使い、毎イベントの dictionary、任意オブジェクトの保持、同期 I/O を避ける。細分化は keyword と provider で選び、全関数の呼び出しを記録しない。EventSource の書き込み方法による boxing・割当も含め、無効時と有効時の負荷を実測する。一定割合以下の負荷は未測定のまま保証しない。

### フレームと処理区間の契約

相関キーは実行を表す `Guid` の ProfileRunId、実行内で再利用しない正の Frame ID、provider 内で再利用しない Span ID と任意の Parent Span ID とする。Span の識別には provider 名も含める。Parent Span ID は同じ provider 内の明示的な親に限り、assembly をまたいだ任意の処理の親子関係を推測しない。異なる provider の処理は ProfileRunId と Frame ID で同じフレームにまとめる。

assembly 間の伝播には標準の `Activity.Current` と `Activity.SetCustomProperty` を使う。ホストは制御 keyword の購読中だけ実行の ProfileRunId を作り、各フレームを標準 `Activity` の相関スコープで囲む。`Lumyte.Profiling.RunId` に boxed Guid、`Lumyte.Profiling.FrameId` に boxed long を設定する。これは新規に追加する内部動作であり、基準コードに既存の伝播機能があるという説明ではない。`ActivitySource.StartActivity` の listener 有無に依存せず、相関用の `new Activity(...).Start()` を使用し、終了時に Dispose して元の Current を復元する。個々の処理段階には Activity を生成しない。フレーム単位の Activity と boxing の負荷は有効時の負荷へ含める。

下流 assembly は `Activity.Current` から Parent を辿り、この二つの標準 custom property を持つ最も近い Activity を読む。これにより共有の内部型、公開引数の変更、新しい IVT は不要である。区間開始時に取得した数値を終了まで保持し、終了時の Current で帰属を変更しない。通常の async 継続では ExecutionContext による伝播を使い、flow を抑制する既存 worker へは開始側で取得した Activity をその work item の内部フィールドで渡す。参照を保持するだけで所有権は移さず、worker がフレーム Activity を Dispose しない。値がない場合は ProfileRunId を空、Frame ID を 0 とし、未帰属として残す。別実行の状態を static callback で捕捉しない。

開始と終了イベントは同じ相関キーを持つ。イベントヘッダーの時刻と thread 情報を使い、非同期継続では終了が別 thread でも対応付ける。例外時も finally 相当で失敗の終了を発行するが、途中からの接続・停止・欠落では片側だけ存在することを許容する。解析器はその区間を未完了に分類し、0ms の正常区間へ補完しない。

CPU 区間は単調時計の経過時間であり、OS がスレッドを実行した CPU 時間ではない。更新、Measure、Arrange、描画データ生成、描画準備、compile、command 記録、submit と、GPU 完了待ち・フレーム枠待ち・owner loop の待機を別の段階として扱う。親区間は子を含み、並列や入れ子の時間を単純加算しない。欠落のない同一 thread の入れ子でのみ子区間の和集合を引いた自己時間を算出する。

作業量は処理要素数、再計算数、cache hit・miss、draw・batch・pass 数、upload byte 数、資源生成数を段階に関連付ける。単位、実測か推定か、差分か現在値か、集計範囲を定義し、取得不能は欠測とする。画面の内容やユーザー文字列・入力値は自動収集しない。

GC 停止・allocation sample・スタックサンプルは標準ランタイムイベントを利用する。スタックのサンプル比率を実 CPU 使用時間の割合、allocation sample を正確な全割当とみなさない。同一 thread の同期区間で明示的に取る割当差分と、全プロセスの割当近似値は別の値とし、スレッド移動した区間の差分は欠測とする。

### 共通イベント契約とモジュールの責務

公開ライブラリ API と既存の診断操作は変更しない。共通 event ID は 1=`StageBegin`、2=`StageEnd`、3=`StageCounter` とし、各 provider が内部の EventSource 型へ定義する。区間は ProfileRunId、Frame ID、Span ID、Parent Span ID、stage ID を持つ。終了は outcome、カウンターは stage ID、counter ID と long の値を持つ。outcome は 0=成功、1=取消、2=失敗、3=省略とする。

stage ID、counter ID、その単位と実測・推定の区別、実際のメソッド内の開始・終了位置、例外・省略時の扱いは各モジュールの判断として固定する。本体に存在しない段階を名前だけで作らず、複数モジュールが同じ処理を独立の計測点として重複発行しない。親子の区間が重なることと、同じ処理を二重に計時することは区別する。

provider ごとの追加 event ID は 4 以降を使う。keyword は EventSource の nested `Keywords` クラスへ `public const EventKeywords` として宣言する。大量の個別対象イベントには各 provider 内で `0x10` 以降を追加できるが、既定の `0xF` へ含めず opt-in とし、意味をモジュール内で定義する。

EventSource の Start/Stop の自動 activity ID 相関には依存せず、上の明示キーで非同期区間を対応付ける。singleton は固定スキーマの発行器だけを所有し、実行スコープ・描画資源・ユーザーオブジェクトを保持しない。既存の observer・Activity・capture の通知条件と資源寿命を維持し、新しいイベントの購読だけでは構造 snapshot を生成しない。

### バッファ、欠落と停止

収集容量は標準ツールの `--buffersize` で指定し、実行を待たせないドロップ型の収集を使う。対応版で buffering mode を指定する場合も Drop を選ぶ。無制限の独自キューやアプリ側のキャプチャ履歴は追加しない。

バッファ上限は記録ファイルの総サイズや実行時間の上限ではない。取得者は duration または明示停止で範囲を制御し、保存先の容量を管理する。解析器は取得可能な lost-event 情報、フレーム番号の欠落、開始・終了不一致を検査する。標準形式から正確な欠落数を取得できない場合は不明とし、完全記録と断定しない。

収集停止はアプリの停止・GPU 完了待ち・資源解放を意味しない。停止前に開始し停止後に終わった区間や遅れて回収した GPU 結果は、記録にない場合がある。有限シナリオで完全な結果が必要なら、ハーネスが既存の終了処理を完了して測定終了 marker を出した後で収集を止める。

### GPU 計測の境界

GPU timestamp は .NET の標準ランタイムイベントだけでは取得できない。基準コードの `tools/benchmarks/drawing2d-incremental/HardwareGpuTimer.cs` は Vulkan 専用のベンチマーク実装であり、これを製品の共通 API や全 backend 対応とみなさない。CPU とカウンターの標準収集を基本とし、GPU 計測は実際に対応した経路だけが結果を EventSource へ発行する。

GPU 結果には Frame ID、submission ID、device・queue、計測境界、timestamp period または周波数、有効 bit 数を含める。非対応・結果未準備・device lost を区別し、submit から完了通知までの CPU 経過時間で代用しない。CPU と GPU の時計を校正しない場合は ID で対応付け、同一時刻軸上の絶対位置を作らない。

query は各フレーム枠が所有し、通常の submission 完了確認後に読み出し・再利用・解放する。追加同期や毎フレーム待ちを計測のために挿入しない。query 数と未回収結果数には実行全体の固定上限を設け、枯渇時は新しい計測を省略する。購読停止後も安全回収が必要な資源は上限へ算入し、再接続のたびに増殖させない。

### 来歴と比較

測定開始・終了 marker で Run ID、シナリオ、ウォームアップと測定の区分、期待フレーム数を残す。来歴は制御用 keyword `0x1` で低頻度に発行し、既存ベンチマークの出力にも残す。接続前に開始 marker が終わっている通常アプリでは、次のフレーム境界で有効化を検知して実行の識別と来歴を再発行する。複数の標準収集セッションが重なっても途中接続を扱えるよう、制御 keyword の購読中は次のフレーム境界で最大 1 秒に一回、同じ来歴を再発行する。解析器は対応する来歴が届くまで実行属性を不明とする。

対象とハーネスの commit、未コミット差分・実行バイナリの hash、build、runtime、OS、CPU、GPU、driver、backend、描画先サイズ、提示方式、同時進行フレーム数、入力 seed と取得した provider・keyword・ツール版を照合する。来歴の発行頻度は制御し、動的名を毎区間登録しない。取得できない情報は不明とする。

中央値・p95・最大値は母集団・件数・単位を示して集計する。個々のフレームと反復平均、直列と複数フレーム並行、headless と提示ありは分ける。詳細計測の有無や provider が違う結果を同条件と扱わず、欠落区間を都合よく除外して改善を主張しない。

## 検討した代替案

- 独自 sink・バッファ・保存形式を用意する: 任意のフレーム数や詳細な保持予算を直接制御できるが、EventPipe の収集責務を再実装し、ランタイム情報との統合も必要になる。標準経路の負荷や機能が実測で不足した部分だけを個別に判断する。
- ActivitySource だけで全区間を記録する: 既存診断との接続は容易だが、細粒度の全フレーム記録に論理 trace の生成負荷とサンプリング契約を持ち込む。論理 trace は維持し、性能調査のイベントは EventSource に分ける。
- 外部サンプラだけで調査する: 関数・GC の解析に有効だが、Lumyte の処理段階、作業量、フレーム帰属は自動で分からない。必要な文脈を EventSource で補う。
- 調査ごとの計測パッチを維持する: 小さな調査には適するが、通常実行との比較と保守が難しい。再利用する主要計測点を本体内へ置く。

## 結果と影響

開発者は標準ツールを対象へ接続する手順で、Lumyte の処理と .NET ランタイムの情報を同じトレースから調べられる。本体側には固定イベントの保守と相関情報の伝播が増えるが、独自収集セッションと保存基盤の保守は不要になる。公開 API と既存の診断権限・通信仕様を維持する。

受け入れ条件は次とする。文書で数値上の性能改善や標準ツールの全環境対応を保証しない。

- 未購読時に追加の時計取得・記録用割当・GPU query がなく、描画結果・例外・資源寿命が変わらない。有効時は同一 build の未購読実行と負荷を比較する。
- `dotnet-trace` の実接続で Lumyte とランタイムの双方のイベントを取得し、型・ID・単位・version・keyword と明示相関を検証する。取得例は使用するツール版で確認する。
- 通常アプリへの途中接続、ウォームアップ、複数実行、非同期継続、例外でフレームと親区間を取り違えない。スタックサンプルを実 CPU 時間と誤表示しない。
- バッファ圧迫・途中停止・終了不一致を解析し、欠落または不明を明示する。収集停止でアプリや GPU 資源を早期解放しない。
- 既存 RenderGraph observer と同時利用して、通知・構造 capture・Activity の条件が変わらず、独立計時が二重化しない。
- GPU 非対応・query 枯渇・結果未準備・device lost で安全回収と固定上限を維持する。CPU の標準収集を GPU 機能の不足で妨げない。
- ベンチマークの期待フレーム数、実取得数、来歴、ウォームアップ除外と集計母集団を照合し、トレース無効の性能比較でも改善を確認する。
