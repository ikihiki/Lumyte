# ADR-ANIMATION-0001: 時間による値の変化と実行タイミングを制御するアニメーション基盤

- 状態: 提案
- 日付: 2026-10-08

## 背景

Lumyte は C# を中心とするゲームエンジンで、Windows、Linux、Browser と複数の描画バックエンドを対象とする。現時点ではアニメーション、シーン、アセットの実装や契約は存在しない。描画 API に依存せず検証できるアニメーションの評価契約を先に定め、後続のエンジン統合と描画実装の境界を明確にする必要がある。

アニメーションシステムは、ボーン制御から UI の位置・サイズ・色・透明度まで、時間によって値が変わる事象と、いつどの変化を起こすかの両方を扱う。代表的な用途は、待機から歩行への切り替え、上半身の動作の重ね合わせ、パネルを表示した後のボタンのフェード、入力に応じた選択状態の変化、複数の演出の同期である。スケルタルアニメーションはこの基盤の利用領域の一つとする。既存コードの移行は発生しない。

## 決定

型付きの値評価、実行タイミングと再生状態の制御、対象への適用を分離する。共通基盤がカーブ・Tween・タイムライン・トリガーによる状態遷移を扱い、ボーンと UI はそれぞれのアダプターで評価結果を利用する。アプリケーションは入力や条件判定をトリガーとして渡し、アニメーション側が定義に従って再生する変化と時刻を決定する。ワールド移動、スキニング、UI レイアウトは各統合層の責務とする。

### 適用範囲と依存方向

実装時の配置は [ADR-0002](../0002-repository-layout.md) の分類ルールに従う `src/Animation/Lumyte.Animation/`、名前空間と NuGet パッケージ名は `Lumyte.Animation` とする。今回の変更ではプロジェクトを作成しない。

```text
入力・ゲームの条件判定 → トリガー
                           ↓
                  再生制御・タイムライン
                           ↓
                    型付きの値評価
                           ↓
                   出力バッチ・イベント
                           ↓
         ボーン／UI／シーンの適用アダプター
                           ↓
                 物理・レイアウト・描画
```

- `Lumyte.Animation` はシーン、ECS、UI フレームワーク、描画、OS、Native ライブラリに依存しない。基本数値型は `System.Numerics` を使用する。
- 共通基盤は値評価、遅延、順次・並列実行、ループ、一時停止、Seek、中断、トリガーによる状態遷移、イベント収集を所有する。
- Engine と UI の統合層は時計の選択、対象とチャネルの対応、評価結果の適用、イベント配送を所有する。Animation から各統合層への依存を作らない。
- ボーン用機能は TRS 評価、通常・加算ブレンド、マスク、クロスフェード、ルートモーション抽出を共通の再生制御に接続する。
- グラフの編集 UI・保存形式、モーフ専用アダプター、IK、リターゲティング、GPU 評価、インポーターと圧縮形式は後続判断とする。値評価と実行タイミングの制御は今回の範囲に含める。

### 共通の値評価と出力

`IAnimationSource<T>` はローカル時刻から T の値を返す純粋な評価契約とする。キーフレームカーブと Tween は同じ契約を実装する。`float`、`Vector2`、`Vector3`、`Vector4`、`Quaternion` の補間を標準提供し、色は統合層が定める色空間の Vector4 として扱う。離散値は Step 補間で切り替え、独自型は `IAnimationInterpolator<T>` で拡張する。型の異なる値を暗黙に変換しない。

Tween の始値と終値は明示的に与える。現在値から開始したい場合は、統合層が開始前に値を読み、始値を固定する。評価中に UI やシーンの値を読み取らず、同じ時刻と入力で同じ値を得られるようにする。補間とイージングは区別し、イージングで正規化時間を変換してから型ごとの補間を行う。標準イージングは `[0, 1]` を保ち、端点は厳密に 0／1 とする。

出力先は型付きの `AnimationChannel<T>` で識別する。チャネルは対象オブジェクトとプロパティの組み合わせに統合層が一意に割り当てる識別子であり、リフレクションによるプロパティ探索を要求しない。評価は `AnimationOutput` へ結果を書き、適用は別段階とする。UI の位置・透明度とスケルトンのポーズを同じタイムラインに配置できるが、各値の適用方法はチャネルの型に応じて異なる。

### 実行タイミングと再生制御

`AnimationTimeline` は開始時刻、値ソース、出力チャネル、終端時の扱いを持つ不変の項目集合とする。開始時刻によって遅延を、前項目の終了時刻への配置によって順次実行を、同じ開始時刻への配置によって並列実行を表す。ネストしたタイムラインは Build 時に時刻を合成して平坦化する。長さは最後の項目またはイベントの終了時刻で決め、全体に正の長さを要求する。

開始前の項目は値を出力しない。`Hold` は終了以降も終値を出力し、`Release` は終端到達の更新で終値を一度出力した後にチャネルを解放する。大きな時間差で開始と終了を飛び越しても終値を評価する。適用先は最後の適用値を保持するため、解放は元値への復元を意味しない。Seek は通過イベントや途中の項目の終了通知を発生させず、移動先で有効な値だけを再評価する。

同一タイムライン内の同じチャネルに複数の項目が寄与する場合は、後から登録した項目を優先する。複数の再生者の出力を一つの AnimationOutput に集める場合も、更新順で後の寄与を優先する。更新順は統合層が固定する。ブレンドが必要な値は明示的なブレンドソースで一つの値へ合成してから出力し、UI に意図しない加算を適用しない。

`AnimationController` は状態名とタイムライン・ループ設定、現在状態とトリガー名から次状態への遷移を保持する。条件判定は入力・ゲーム側で行い、Trigger を送る。制御層は次の Update の開始時にキュー順でトリガーを処理し、遷移先の再生を時刻 0 から開始する。対応する遷移がないトリガーは消費して無視し、同じ状態・トリガーの重複登録は拒否する。トリガー列は有限とし、イベントからの新しいトリガーは配送後の次更新で処理する。評価中に再帰的に遷移しない。

中断は現在の再生を Cancel して新しい再生を開始する。既に適用した値は保持し、新しい変化の始値が必要なら統合層がその値を明示的に取得する。ボーンのクロスフェードは専用のポーズ合成を使用する。汎用コントローラーの状態遷移は即時切り替えとし、UI の滑らかな遷移は明示した Tween で構成する。

### 共通の公開 API

以下は `Lumyte.Animation` の主要な公開契約とする。補間器、値ソース、タイムラインは不変とし、独自実装にも純粋な評価を要求する。

| 公開 API | 役割・契約 |
| --- | --- |
| `IAnimationSource<T>`: `double Duration { get; }`, `T Sample(double time)` | 有限かつ正の Duration と `[0, Duration]` の純粋な時刻評価。対象に副作用を起こさない |
| `AnimationKey<T>(double time, T value)` | 時刻と値の読み取り専用レコード。カーブ内の時刻は厳密に昇順 |
| `IAnimationInterpolator<T>.Interpolate(T from, T to, float amount)` | 正規化時間に対応する型固有の補間。離散値の Step もこの契約で提供する |
| `AnimationCurve<T>(double duration, IReadOnlyList<AnimationKey<T>> keys, IAnimationInterpolator<T> interpolator)` | キーを検証・コピーする。最低一つのキーを要求し、端の値を保持する |
| `Tween<T>(T from, T to, double duration, IAnimationInterpolator<T> interpolator, AnimationEasing easing)` | 明示した端点間を変化させる値ソース。標準 easing は Linear、EaseIn、EaseOut、EaseInOut |
| `AnimationChannel<T>.Create()` | プロセス内で一意な型付きチャネルを作る。統合層が対象との対応を保持する |
| `AnimationTimelineBuilder.Add<T>(double start, IAnimationSource<T> source, AnimationChannel<T> channel, AnimationFillMode fill = AnimationFillMode.Hold)` | 時刻・値・出力を登録する。start は有限かつ 0 以上。登録順を保持する |
| `AnimationTimelineBuilder.AddTimeline(double start, AnimationTimeline timeline)` / `AddEvent(double time, string name, string? payload)` | 既存タイムラインまたはイベントを配置する |
| `AnimationTimeline AnimationTimelineBuilder.Build()` | 入力を検証して不変定義を生成する。Build 後の Builder 変更は生成済み定義に影響しない |
| `AnimationOutput.Clear()` / `bool TryGet<T>(AnimationChannel<T> channel, out T value)` | 統合層がフレーム開始時にクリアし、全評価後に型付きの結果を取得する。未出力は false |
| `AnimationPlayback(AnimationTimeline timeline, AnimationWrapMode wrapMode = AnimationWrapMode.Once)` | 再生状態を所有する。State、Time、Speed、Play／Pause／Stop／Seek は後述の時間契約に従う |
| `AnimationPlaybackState AnimationPlayback.State { get; }` / `double Time { get; }` / `double Speed { get; set; }` | 現在状態と時刻、有限かつ 0 以上の速度を公開する |
| `void AnimationPlayback.Play()` / `Pause()` / `Stop()` / `Seek(double time)` | 開始・一時停止・停止・時刻移動。Seek は `[0, timeline.Duration]` を要求する |
| `void AnimationPlayback.Cancel()` | Cancelled に移行し、以後の出力とイベントを停止する。Play は 0 から再開する |
| `bool AnimationPlayback.Update(double deltaSeconds, AnimationOutput output, ICollection<AnimationEventOccurrence> events)` | 有効な値と通過イベントを追記し、この更新で完了した場合だけ true を返す |
| `AnimationState(AnimationTimeline timeline, AnimationWrapMode wrapMode)` | 状態の再生定義。歩行は Loop、UI の表示は Once を選べる |
| `AnimationController(string initialState, IReadOnlyDictionary<string, AnimationState> states, IReadOnlyList<AnimationTransition> transitions)` | 定義を検証・コピーして初期状態の再生を開始する。Once の状態は完了後も遷移待ちする |
| `AnimationTransition(string from, string trigger, string to)` | 登録済み状態間の遷移を定義する |
| `void AnimationController.Trigger(string trigger)` / `void Update(double deltaSeconds, AnimationOutput output, ICollection<AnimationEventOccurrence> events)` | トリガーをキューに積み、更新開始時に遷移と現在状態の再生を処理する。`string CurrentState` を公開する |

AnimationTimeline は `double Duration` を公開する。ビルダーの不正時刻・非有限値は ArgumentOutOfRangeException、未登録状態・重複遷移・長さ 0 の定義は ArgumentException、必須の null は ArgumentNullException とし、Build／Controller 作成時に検出する。

出力は再利用可能な型付き格納領域とし、object への毎更新のボックス化を避ける実装を目標とする。出力は一更新に一スレッドが操作し、読み取りと対象への適用は評価完了後に行う。独自ソースの例外時はその更新の出力を適用せず、統合層がエラーを扱う。更新前の内部状態への自動ロールバックは保証しない。

### 利用例と更新順序

UI パネルの表示は「時刻 0 から位置を 0.2 秒で移動」「時刻 0 から透明度を 0.2 秒で変化」「時刻 0.2 からボタンを 0.1 秒でフェード」というタイムラインで表す。`Open` トリガーが表示状態を選び、`Close` が非表示状態を選ぶ。プレイヤーのボーン制御では、`Walk` トリガーが歩行タイムラインを選び、その項目がポーズチャネルへ値を出力する。

更新は、入力と条件判定、トリガー処理、時計の前進、値評価と競合解決、対象への適用、イベント配送の順とする。UI は適用後にレイアウトを更新し、ボーンはポーズ合成後に物理と描画へ渡す。

### ボーン用のデータモデル

| 型 | 表現と不変条件 |
| --- | --- |
| `JointTransform` | 平行移動 `Vector3`、回転 `Quaternion`、拡縮 `Vector3` の値型。有限値と正規化された回転を保持する。せん断は扱わない |
| `Skeleton` | 名前、親インデックス、レスト時のローカル TRS。親は子より前に配置し、単一ルートの親を `-1` とする。名前はスケルトン内で一意 |
| `VectorKey` / `RotationKey` | 秒単位の `double Time` と値。トラック内の時刻は厳密に昇順、同時刻の重複を禁止する |
| `JointTrack` | `int JointIndex` と任意の平行移動・回転・拡縮トラック。各成分に `Step` または `Linear` の補間方法を指定する |
| `SkeletalClip` | `IAnimationSource<ReadOnlyPose>` を実装するポーズソース。対象 `Skeleton`、有限かつ正の `double Duration`、トラックとイベント。キーとイベントの時刻は `[0, Duration]`、同じボーンのトラックは一つ |
| `AnimationEvent` | `double Time`、`string Name`、任意の文字列ペイロード。クリップ内の安定した格納順を持つ |
| `PoseBuffer` | 同一スケルトンのボーン数と一致するローカル TRS の可変バッファ |
| `JointMask` | 対象スケルトンとボーンごとの `[0, 1]` の重み。子への自動伝播はせず、作成側が必要な子を指定する |
| `RootMotionDelta` | 対象ルートのローカル空間における平行移動と回転の差分。拡縮は抽出しない |

スケルトン、クリップ、マスクのコンストラクターは入力コレクションをコピーして検証し、可変配列を公開しない。互換性は同じ `Skeleton` インスタンスであることによって判断し、名前だけの一致による暗黙のリターゲティングをしない。インポート時の座標系、単位、ボーン対応の正規化はアセット側の責務とする。

未指定のトラック成分にはレスト値を使用する。最初のキー以前と最後のキー以後は端の値を保持する。平行移動・拡縮の `Linear` は線形補間、回転は最短経路の Slerp と正規化を使用する。ループ境界をまたぐキー補間は行わず、継ぎ目の連続性はクリップ作成側が保証する。

### ボーン用の公開 API と契約

ボーン用 API も `Lumyte.Animation` に属する。`ReadOnlyPose` はスケルトンと読み取り専用の TRS を持ち、可変配列を公開しない。SkeletalClip の Sample は独立した不変ポーズを返し、ポーズチャネルを通じて汎用タイムラインに配置できる。定常更新では SkeletalSampler のバッファ評価経路を使用できる。汎用タイムラインへ配置する場合、クリップ内イベントは自動伝播せず、統合層が開始時刻を加えて AddEvent に登録する。ボーン専用再生ではクリップのイベントを直接収集する。二つの経路を同じ再生へ併用しない。コレクション要素は前節の値を公開する読み取り専用型とする。

| 公開 API | 役割・契約 |
| --- | --- |
| `Skeleton(IReadOnlyList<string> names, IReadOnlyList<int> parents, IReadOnlyList<JointTransform> restPose)` | 階層を検証してコピーする。`int JointCount` と読み取り専用の名前・親・レスト値を公開する |
| `SkeletalClip(Skeleton skeleton, double duration, IReadOnlyList<JointTrack> tracks, IReadOnlyList<AnimationEvent> events)` | トラック、時刻、値を検証してコピーする。`Skeleton Skeleton`、`double Duration` を公開する |
| `JointMask(Skeleton skeleton, IReadOnlyList<float> weights)` | ボーン数と重みを検証してコピーする |
| `PoseBuffer(Skeleton skeleton)` | レストポーズで初期化する。`Skeleton Skeleton`、`Span<JointTransform> LocalTransforms` を公開する |
| `void SkeletalSampler.Sample(SkeletalClip clip, double time, PoseBuffer destination)` | `[0, Duration]` の絶対時刻を評価し、出力を全成分上書きする。時間を進めず、イベントを発生させない |
| `void PoseBlender.Blend(PoseBuffer from, PoseBuffer to, float weight, JointMask? mask, PoseBuffer destination)` | 通常ブレンド。重みは `[0, 1]`。`null` マスクは全ボーンに重み 1 を適用する |
| `void PoseBlender.Additive(PoseBuffer basis, PoseBuffer sample, PoseBuffer reference, float weight, JointMask? mask, PoseBuffer destination)` | 参照ポーズとの差分を基底ポーズに加える。すべて同一スケルトン、重みは `[0, 1]` |
| `SkeletalPlayback(SkeletalClip clip, AnimationWrapMode wrapMode = AnimationWrapMode.Loop)` | 共通の AnimationPlayback と同じ時間・イベント制御を使い、ボーン専用のバッファ評価とルート抽出を行う。初期状態は `Stopped`、時刻は 0 |
| `double SkeletalPlayback.Speed { get; set; }` | 有限かつ 0 以上。0 は再生状態を維持したまま時間を停止する。逆再生は初期範囲外 |
| `AnimationPlaybackState SkeletalPlayback.State { get; }` / `double Time { get; }` | `Stopped`、`Playing`、`Paused`、`Completed`、`Cancelled` と現在のサンプル時刻 |
| `void SkeletalPlayback.Play()` / `Pause()` / `Stop()` | 開始・一時停止・停止。停止は時刻を 0 に戻す。完了後の Play は 0 から再開する |
| `void SkeletalPlayback.Cancel()` | 再生を中断し、以後の Update は出力ポーズを変更せず、イベントと移動差分を返さない |
| `void SkeletalPlayback.Seek(double time)` | `[0, Duration]` 内へ移動する。イベントや移動差分を発生させず、差分の基準時刻を更新する |
| `RootMotionMode SkeletalPlayback.RootMotion { get; set; }` | 既定は `None`。`Extract` はスケルトンの単一ルートを抽出対象にする |
| `AnimationUpdateResult SkeletalPlayback.Update(double deltaSeconds, PoseBuffer destination, ICollection<AnimationEventOccurrence> events)` | 時間を進め、ポーズを上書きし、通過イベントをコレクションに追記する。結果に今回の `RootMotionDelta` と `bool CompletedThisUpdate` を返す |
| `AnimationEventOccurrence` | 元イベント、`long LoopIndex`、今回の更新開始からの実時間オフセットを公開する値型 |
| `CrossFade(Skeleton skeleton)` / `Start(PoseBuffer current, double durationSeconds)` / `Apply(PoseBuffer target, double deltaSeconds, PoseBuffer destination)` | 現在の合成ポーズをコピーし、時間とともに target へ補間する。`bool IsActive` を公開する |

`AnimationWrapMode` は `Once` と `Loop`、`RootMotionMode` は `None` と `Extract` とする。負の時間差、NaN、無限大、範囲外の重み・時刻・値は `ArgumentOutOfRangeException`、階層不整合・スケルトン不一致は `ArgumentException`、必須の null は `ArgumentNullException` とする。通常の引数エラーは再生状態や出力の変更前に検出する。

Sampler と Blender は入力を変更しない。Blender の出力は入力と同じバッファでもよい。プレイヤーとバッファは利用側が所有する通常の Managed オブジェクトで、Native ハンドルや `Dispose` を必要としない。バッファの Span は同時更新や非同期処理をまたいで保持しない。

### 共通の時間、状態、イベント

- 時計を内部で取得せず、利用側が秒単位の `deltaSeconds >= 0` を渡す。ゲームとボーンはシミュレーション時計、UI はゲームの一時停止に影響されない時計など、統合層が明示的に時計を選ぶ。同じ再生者を複数の更新ループから進めない。描画だけの再評価では時間を進めない。Browser の停止復帰などによる大きな時間差の制限は Engine の方針とする。
- `Play` は Playing／Paused では現在位置を保ち、Cancelled では 0 から再開する。`Pause` は Playing のみ変更する。`Stop` はどの状態からも停止して時刻を 0 に戻す。
- ボーン専用 Update は Stopped／Paused／Completed でも現在位置を評価する。汎用 Update は Stopped／Cancelled では出力せず、Paused では停止位置の有効項目、Completed では Hold 項目だけを再評価する。いずれもイベントと移動差分を返さない。`deltaSeconds == 0` も同様。Once は終端に達した更新だけで完了を通知し、終端ポーズを保持する。
- Loop は非負の非折り返し累積時間を保持し、サンプル時刻を `[0, Duration)` に折り返す。イベントと移動差分は折り返し前の区間を使って計算する。Loop の Seek(Duration) は次周の 0 と同等に扱う。
- イベントは通過区間 `(前回時刻, 今回時刻]` で配送する。時刻 0 のイベントは停止からの初回の正の前進と各周の開始で一度ずつ配送する。Seek 後の始点イベントは配送しない。
- ループ境界では前周の Duration のイベント、その後に次周の 0 のイベントを配送する。同一時刻では格納順を保つ。複数周を進む Update も通過した全イベントを返し、暗黙の省略をしない。長時間差では出力量が増えるため、利用側が時間差を制限する。
- 評価中にゲームコードのコールバックを呼ばない。呼び出し側が渡した追記先は再入しないコレクションとし、Engine が評価終了後に配送する。クロスフェード中に複数プレイヤーを更新するとき、イベント配送元はゲーム側が明示的に選び、足音などの重複を防ぐ。

### ブレンドと遷移

通常ブレンドでは各ボーンの有効重みを `weight * maskWeight` とし、TRS の平行移動・拡縮を Lerp、回転を最短経路の Slerp で補間する。多数のクリップを混ぜる場合は呼び出し側が順序と重みを定め、二入力の演算を合成する。演算順序による結果の違いを許容し、暗黙の重み正規化は行わない。

加算ブレンドでは平行移動を `sample - reference`、拡縮を成分ごとの `sample / reference`、回転を参照からサンプルへの差分として扱う。差分回転 D は `referenceRotation * D = sampleRotation` を満たすものとし、`basisRotation * Slerp(identity, D, 有効重み)` を出力する。拡縮は `basisScale * Lerp(one, 比率, 有効重み)` とする。参照拡縮が 0 の成分は引数エラーとする。出力回転は正規化する。

CrossFade は Start の時点の合成ポーズを保持し、毎更新の target に向けて線形に重みを進める。途中の再 Start は直前の出力ポーズをコピーして連続性を保つ。期間は有限かつ 0 以上、0 は即時切り替えとする。Start は入力をコピーし、Apply は入力と同じ出力バッファを許容する。これは遷移元の再生を継続する二プレイヤーブレンドとは異なる選択であり、開始時ポーズからの遷移を初期契約とする。

### ルートモーションと描画への受け渡し

`None` はルートの TRS をそのままポーズへ含め、移動差分を恒等値にする。`Extract` はルートの平行移動・回転をレスト値へ戻し、同じ動きを `RootMotionDelta` に抽出する。ルート拡縮はポーズに残す。Seek、Stop、再開直後に過去の移動を再適用しない。

差分は時間順に剛体変換として合成し、前回のルート変換 P と今回の変換 C に対して `P * delta = C` を満たすものとする。ループをまたぐ場合は終端まで、各完全周、次周の開始以降に区間を分け、単純な折り返し時刻の差による巻き戻りを防ぐ。Once は終端までの差分だけを返す。

初期契約では一更新につき一つのプレイヤーだけを移動の権威とする。クロスフェードや加算レイヤーの移動差分を暗黙に合成しない。複数クリップのルートモーションブレンドは後続 ADR の対象とする。Engine が抽出量を物理・衝突判定へ渡し、実際の移動を決定する。

描画統合層は最終ローカル TRS から親順にモデル空間の階層変換を構築し、メッシュ側の逆バインド行列と合わせてスキニング行列を生成する。行列の格納・乗算規約、GPU 転送、頂点ウェイトとバッファ寿命は Graphics／アセット側で定める。Animation はワールド行列と逆バインド行列を所有しない。

### スレッド、環境、性能

不変アセットと純粋な値ソースはスレッド間で共有できる。Controller、Playback、AnimationOutput と適用先は一更新に一つのスレッドだけが操作する。UI の適用はそのフレームワークが要求するスレッドで実行する。プレイヤー、CrossFade、PoseBuffer、イベント追記先は一更新中に一つのスレッドだけが操作する。異なるキャラクターや UI の独立した再生を並列評価できるが、実行スケジューラーは Engine の責務であり Browser に並列実行を要求しない。同一入力と更新列の再現性を目指す一方、異なる CPU・ランタイム間の浮動小数点のビット一致は保証しない。

| 環境 | 評価方式 | 制約 |
| --- | --- | --- |
| Windows / Linux | Managed CPU 評価 | DirectX／Vulkan の利用可否に依存しない |
| Browser | 同じ Managed CPU 評価 | 単一スレッドで動作でき、Native と動的コード生成を要求しない |

サンプル探索は二分探索を基準とし、クリップを再生者ごとにコピーしない。通常更新では利用側がポーズとイベントの容量を再利用し、内部の一時バッファも再利用する。イベント出力先の容量拡張と初期化を除き、スカラー系の共通評価とボーン専用バッファ評価の定常更新の Managed 割り当てゼロを検証目標とする。不変 ReadOnlyPose を返す汎用ポーズ経路は生成コストを伴うため別途測定し、必要なら所有権を明示したバッファ出力契約を後続で定める。性能値は未測定であり、GPU 化を前提にしない。

## 検討した代替案

### 描画バックエンド内でクリップを評価する

GPU と近い位置で処理できるが、バックエンドごとに再生・イベントの契約が重複する。描画しないサーバーや単体テストにも使える CPU 評価を先に定める。GPU スキニング自体はこの判断で禁止しない。

### シーンのコンポーネントだけを公開する

利用方法は簡単になるが、未決定の ECS やシーンに評価処理まで依存する。再利用できる評価ライブラリと Engine の統合層を分離する。

### ボーン評価と UI Tween を別の再生システムにする

用途固有の最適化はしやすいが、時間・ループ・イベント・中断・順次実行の仕様が重複し、複数領域の演出を同期しにくい。値の型と適用は分け、時間と実行制御を共通化する。

### いつ何を再生するかをすべて利用側へ任せる

評価ライブラリを小さくできるが、遅延、順次・並列実行、トリガーによる切り替えを利用側が毎回実装する必要がある。タイムラインと基本コントローラーを今回の責務とし、編集・保存形式や高度な条件グラフだけを後続判断とする。

### Native の既存アニメーションライブラリを採用する

圧縮や SIMD 最適化の利点があるが、Browser 対応、配布、相互運用、所有権の契約が必要になる。初期段階では Managed 実装の検証容易性を優先し、測定で必要性を確認してから再検討する。

## 結果と影響

- ボーン、UI、その他の型付きプロパティ変化を同じ時間・再生制御で扱い、複数領域の順序と同期を定義できる。
- 値評価と実行制御を描画なしで検証でき、同じ定義を複数対象と環境で共有できる。
- シーンと描画の契約が未確定でも評価ライブラリの設計を進められる。
- アニメーション側が実行タイミングと遷移を制御し、ゲーム・UI 側が条件判定、時計、値の適用と副作用を制御できる。
- バッファ管理、レイヤーの合成順、イベント配送元、移動の権威を利用側が指定する必要がある。
- CPU 評価と二入力のブレンドは最初の実装を単純にするが、大規模群衆や複雑なグラフの性能は別途測定が必要になる。
- 本 ADR は設計提案であり、実装・性能・Browser 動作を検証済みとするものではない。

## 検証方針

採用後の実装では以下を受け入れ条件とする。

- float／Vector／Quaternion／離散値のカーブと Tween、イージング端点、型の異なるチャネルと独自型の補間を検証する。
- UI の位置と透明度、ボタンの遅延フェード、ボーンのポーズ出力を同じタイムラインへ配置し、順次・並列・ネストの開始／終了順序を検証する。
- 同一チャネルの優先順、Hold／Release、開始終了を飛び越す更新、Cancel と再開始、Seek が中間イベントを配送しないことを確認する。
- Open／Close／Walk トリガー、未登録遷移、同一更新の複数トリガー、イベント配送からの翌更新の遷移を検証する。
- ゲーム時計を停止したまま UI 時計だけを進め、対象への適用が評価後に一度行われることを確認する。

- キー境界、端の保持、欠けた成分、Step／Linear、反対符号の同一回転を期待する TRS と比較する。
- 通常・加算ブレンドの重み 0／1、ボーンマスク、参照ポーズの恒等差分、入力と出力の同一バッファを検証する。
- Once の終端、Pause／Play／Stop／Seek、速度 0、クロスフェード中断と期間 0 を検証する。
- 0／Duration／同時刻イベント、複数周、Seek 後、時間を小分けした更新と一括更新で、イベント順と重複・欠落を検証する。
- ルートの移動と回転を含むループクリップで、複数周の差分合成と小分け更新の結果を許容誤差内で比較する。物理側の実移動は抽出量と独立して検証する。
- 不正階層、重複トラック、不正数値、スケルトン不一致が通常の引数エラーで部分更新を起こさないことを確認する。
- 共通の既知入力を Windows、Linux、Browser で許容誤差内で比較する。描画統合時にはレストポーズのスキニングと親子階層を既知メッシュで確認する。
- 100 ボーン・4 入力クリップ・100 キャラクターを測定用の基準として、ウォームアップ後の評価時間と割り当て量を記録する。イベントの有無を分け、ハードウェアとランタイムを併記する。合格時間の予算は Engine のフレーム予算と合わせて後続で定める。

## 別途決定する事項

- アセットのインポート・座標規約・保存形式・バージョニング・圧縮と再ロード。
- Engine のコンポーネント API、固定更新と描画補間、イベントの配送先。
- 状態機械・タイムラインの編集と保存形式、高度な条件グラフ、ブレンドツリー、同期グループと複数クリップのルートモーション合成。
- IK、リターゲティング、モーフ専用アダプター、各 UI フレームワークのチャネルとレイアウト統合。
- Graphics の行列規約、GPU スキニングの公開契約、ボーン数の制限と評価 LOD。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
