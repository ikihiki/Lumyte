# ADR-ANIMATION-0001: 描画から独立したポーズ評価とアニメーション再生

- 状態: 提案
- 日付: 2026-10-08

## 背景

Lumyte は C# を中心とするゲームエンジンで、Windows、Linux、Browser と複数の描画バックエンドを対象とする。現時点ではアニメーション、シーン、アセットの実装や契約は存在しない。描画 API に依存せず検証できるアニメーションの評価契約を先に定め、後続のエンジン統合と描画実装の境界を明確にする必要がある。

代表的な用途は、キャラクターの待機と歩行の切り替え、移動速度に応じたクリップのブレンド、上半身だけの動作の重ね合わせ、足音などの再生イベント、物理側で適用する移動量の抽出である。本 ADR はこれらを支えるスケルタルアニメーションの実行モデルを提案する。既存コードの移行は発生しない。

## 決定

不変のスケルトン・クリップと、インスタンスごとの可変の再生状態を分離する。C# の CPU 評価でローカルポーズを生成し、ブレンドした結果をエンジンと描画へ渡す。状態の選択はゲーム側、時間とポーズの評価はアニメーション側、ワールド移動とスキニングはそれぞれエンジン・物理側と描画側の責務とする。

### 適用範囲と依存方向

実装時の配置は [ADR-0002](../0002-repository-layout.md) の分類ルールに従う `src/Animation/Lumyte.Animation/`、名前空間と NuGet パッケージ名は `Lumyte.Animation` とする。今回の変更ではプロジェクトを作成しない。

```text
ゲーム側の状態選択
        ↓
Lumyte.Engine → Lumyte.Animation → System.Numerics
        ↓              ↓
シーン・物理       ローカルポーズとイベント
        ↓
描画への受け渡し → Lumyte.Graphics → 各描画バックエンド
```

- `Lumyte.Animation` はシーン、ECS、描画、OS、Native ライブラリに依存しない。基本数値型は `System.Numerics` を使用する。
- Engine の統合層が更新順序、対象オブジェクトとの対応、イベント配送、描画用データへの変換を所有する。上図の受け渡しはデータの流れであり、Animation から Graphics への参照を意味しない。
- 初期範囲は TRS のキーフレーム評価、通常ブレンド、加算ブレンド、ボーンマスク、クロスフェード、ループ、イベント、単一プレイヤーのルートモーション抽出とする。
- 汎用プロパティ／UI の Tween、モーフ、IK、リターゲティング、状態機械・ブレンドツリーのアセット、GPU 評価、インポーターと圧縮形式は後続判断とする。

### データモデル

| 型 | 表現と不変条件 |
| --- | --- |
| `JointTransform` | 平行移動 `Vector3`、回転 `Quaternion`、拡縮 `Vector3` の値型。有限値と正規化された回転を保持する。せん断は扱わない |
| `Skeleton` | 名前、親インデックス、レスト時のローカル TRS。親は子より前に配置し、単一ルートの親を `-1` とする。名前はスケルトン内で一意 |
| `VectorKey` / `RotationKey` | 秒単位の `double Time` と値。トラック内の時刻は厳密に昇順、同時刻の重複を禁止する |
| `JointTrack` | `int JointIndex` と任意の平行移動・回転・拡縮トラック。各成分に `Step` または `Linear` の補間方法を指定する |
| `AnimationClip` | 対象 `Skeleton`、有限かつ正の `double Duration`、トラックとイベント。キーとイベントの時刻は `[0, Duration]`、同じボーンのトラックは一つ |
| `AnimationEvent` | `double Time`、`string Name`、任意の文字列ペイロード。クリップ内の安定した格納順を持つ |
| `PoseBuffer` | 同一スケルトンのボーン数と一致するローカル TRS の可変バッファ |
| `JointMask` | 対象スケルトンとボーンごとの `[0, 1]` の重み。子への自動伝播はせず、作成側が必要な子を指定する |
| `RootMotionDelta` | 対象ルートのローカル空間における平行移動と回転の差分。拡縮は抽出しない |

スケルトン、クリップ、マスクのコンストラクターは入力コレクションをコピーして検証し、可変配列を公開しない。互換性は同じ `Skeleton` インスタンスであることによって判断し、名前だけの一致による暗黙のリターゲティングをしない。インポート時の座標系、単位、ボーン対応の正規化はアセット側の責務とする。

未指定のトラック成分にはレスト値を使用する。最初のキー以前と最後のキー以後は端の値を保持する。平行移動・拡縮の `Linear` は線形補間、回転は最短経路の Slerp と正規化を使用する。ループ境界をまたぐキー補間は行わず、継ぎ目の連続性はクリップ作成側が保証する。

### 公開 API と契約

以下は判断時点の主要 API であり、すべて `Lumyte.Animation` に属する。コレクション要素は前節の値を公開する読み取り専用型とする。

| 公開 API | 役割・契約 |
| --- | --- |
| `Skeleton(IReadOnlyList<string> names, IReadOnlyList<int> parents, IReadOnlyList<JointTransform> restPose)` | 階層を検証してコピーする。`int JointCount` と読み取り専用の名前・親・レスト値を公開する |
| `AnimationClip(Skeleton skeleton, double duration, IReadOnlyList<JointTrack> tracks, IReadOnlyList<AnimationEvent> events)` | トラック、時刻、値を検証してコピーする。`Skeleton Skeleton`、`double Duration` を公開する |
| `JointMask(Skeleton skeleton, IReadOnlyList<float> weights)` | ボーン数と重みを検証してコピーする |
| `PoseBuffer(Skeleton skeleton)` | レストポーズで初期化する。`Skeleton Skeleton`、`Span<JointTransform> LocalTransforms` を公開する |
| `void AnimationSampler.Sample(AnimationClip clip, double time, PoseBuffer destination)` | `[0, Duration]` の絶対時刻を評価し、出力を全成分上書きする。時間を進めず、イベントを発生させない |
| `void PoseBlender.Blend(PoseBuffer from, PoseBuffer to, float weight, JointMask? mask, PoseBuffer destination)` | 通常ブレンド。重みは `[0, 1]`。`null` マスクは全ボーンに重み 1 を適用する |
| `void PoseBlender.Additive(PoseBuffer basis, PoseBuffer sample, PoseBuffer reference, float weight, JointMask? mask, PoseBuffer destination)` | 参照ポーズとの差分を基底ポーズに加える。すべて同一スケルトン、重みは `[0, 1]` |
| `AnimationPlayer(AnimationClip clip, AnimationWrapMode wrapMode = AnimationWrapMode.Loop)` | 独立した再生状態を作る。初期状態は `Stopped`、時刻は 0 |
| `double AnimationPlayer.Speed { get; set; }` | 有限かつ 0 以上。0 は再生状態を維持したまま時間を停止する。逆再生は初期範囲外 |
| `AnimationPlaybackState AnimationPlayer.State { get; }` / `double Time { get; }` | `Stopped`、`Playing`、`Paused`、`Completed` と現在のサンプル時刻 |
| `void AnimationPlayer.Play()` / `Pause()` / `Stop()` | 開始・一時停止・停止。停止は時刻を 0 に戻す。完了後の Play は 0 から再開する |
| `void AnimationPlayer.Seek(double time)` | `[0, Duration]` 内へ移動する。イベントや移動差分を発生させず、差分の基準時刻を更新する |
| `RootMotionMode AnimationPlayer.RootMotion { get; set; }` | 既定は `None`。`Extract` はスケルトンの単一ルートを抽出対象にする |
| `AnimationUpdateResult AnimationPlayer.Update(double deltaSeconds, PoseBuffer destination, ICollection<AnimationEventOccurrence> events)` | 時間を進め、ポーズを上書きし、通過イベントをコレクションに追記する。結果に今回の `RootMotionDelta` と `bool CompletedThisUpdate` を返す |
| `AnimationEventOccurrence` | 元イベント、`long LoopIndex`、今回の更新開始からの実時間オフセットを公開する値型 |
| `CrossFade(Skeleton skeleton)` / `Start(PoseBuffer current, double durationSeconds)` / `Apply(PoseBuffer target, double deltaSeconds, PoseBuffer destination)` | 現在の合成ポーズをコピーし、時間とともに target へ補間する。`bool IsActive` を公開する |

`AnimationWrapMode` は `Once` と `Loop`、`RootMotionMode` は `None` と `Extract` とする。負の時間差、NaN、無限大、範囲外の重み・時刻・値は `ArgumentOutOfRangeException`、階層不整合・スケルトン不一致は `ArgumentException`、必須の null は `ArgumentNullException` とする。通常の引数エラーは再生状態や出力の変更前に検出する。

Sampler と Blender は入力を変更しない。Blender の出力は入力と同じバッファでもよい。プレイヤーとバッファは利用側が所有する通常の Managed オブジェクトで、Native ハンドルや `Dispose` を必要としない。バッファの Span は同時更新や非同期処理をまたいで保持しない。

### 時間、状態、イベント

- 時計を内部で取得せず、利用側が秒単位の `deltaSeconds >= 0` を渡す。Engine はシミュレーション更新で時間を進め、描画だけの再評価では進めない。Browser の停止復帰などによる大きな時間差の制限は Engine の方針とする。
- `Play` は Playing／Paused では現在位置を保つ。`Pause` は Playing のみ変更する。`Stop` はどの状態からも停止して時刻を 0 に戻す。
- Stopped／Paused／Completed の Update は現在位置を評価するが、イベントと移動差分を返さない。`deltaSeconds == 0` も同様。Once は終端に達した更新だけで完了を通知し、終端ポーズを保持する。
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

不変アセットはスレッド間で共有できる。プレイヤー、CrossFade、PoseBuffer、イベント追記先は一更新中に一つのスレッドだけが操作する。異なるキャラクターを並列評価できるが、実行スケジューラーは Engine の責務であり Browser に並列実行を要求しない。同一入力と更新列の再現性を目指す一方、異なる CPU・ランタイム間の浮動小数点のビット一致は保証しない。

| 環境 | 評価方式 | 制約 |
| --- | --- | --- |
| Windows / Linux | Managed CPU 評価 | DirectX／Vulkan の利用可否に依存しない |
| Browser | 同じ Managed CPU 評価 | 単一スレッドで動作でき、Native と動的コード生成を要求しない |

サンプル探索は二分探索を基準とし、クリップを再生者ごとにコピーしない。通常更新では利用側がポーズとイベントの容量を再利用し、内部の一時バッファも再利用する。イベント出力先の容量拡張と初期化を除き、定常更新の Managed 割り当てゼロを検証目標とする。性能値は未測定であり、GPU 化を前提にしない。

## 検討した代替案

### 描画バックエンド内でクリップを評価する

GPU と近い位置で処理できるが、バックエンドごとに再生・イベントの契約が重複する。描画しないサーバーや単体テストにも使える CPU 評価を先に定める。GPU スキニング自体はこの判断で禁止しない。

### シーンのコンポーネントだけを公開する

利用方法は簡単になるが、未決定の ECS やシーンに評価処理まで依存する。再利用できる評価ライブラリと Engine の統合層を分離する。

### 最初から汎用アニメーショングラフと状態機械を導入する

複雑な制御をアセット化できる一方、パラメーター、遷移優先度、編集・保存形式の判断が増える。まず明示的なプレイヤーとポーズ演算を公開し、その上に後続の制御層を構築する。

### Native の既存アニメーションライブラリを採用する

圧縮や SIMD 最適化の利点があるが、Browser 対応、配布、相互運用、所有権の契約が必要になる。初期段階では Managed 実装の検証容易性を優先し、測定で必要性を確認してから再検討する。

## 結果と影響

- 再生とポーズ評価を描画なしで検証でき、同じクリップを複数キャラクターと環境で共有できる。
- シーンと描画の契約が未確定でも評価ライブラリの設計を進められる。
- 明示的な時間とイベント配送により、ゲーム側が更新順と副作用を制御できる。
- バッファ管理、レイヤーの合成順、イベント配送元、移動の権威を利用側が指定する必要がある。
- CPU 評価と二入力のブレンドは最初の実装を単純にするが、大規模群衆や複雑なグラフの性能は別途測定が必要になる。
- 本 ADR は設計提案であり、実装・性能・Browser 動作を検証済みとするものではない。

## 検証方針

採用後の実装では以下を受け入れ条件とする。

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
- 状態機械、ブレンドツリー、同期グループと複数クリップのルートモーション合成。
- IK、リターゲティング、モーフ、汎用プロパティのアニメーション。
- Graphics の行列規約、GPU スキニングの公開契約、ボーン数の制限と評価 LOD。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
