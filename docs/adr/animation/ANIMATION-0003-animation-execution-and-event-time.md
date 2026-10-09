# ADR-ANIMATION-0003: 実行者を返す Build と状態マーカーの絶対発生時刻

- 状態: 採用
- 日付: 2026-10-09
- 置換元: [ADR-ANIMATION-0002](ANIMATION-0002-animation-state-machine.md)

## 背景

従来のアニメーション Builder の Build は不変定義を返すため、その後に実行者の生成と Start が必要だった。汎用状態機械の Build(context) は初期入場を終えた実行者を返しており、同じ Build という名前でも利用を開始できる段階が異なる。Composition ノードにも、ネストした Timeline の構築から再生開始までをまとめる入口が必要である。

状態マーカーの UpdateOffset は内部の前回イベント走査基準からの相対時間である。この基準は公開されず、Pause 中の Update では保持される。消費側が別に時計を読み直しても、内部で取得した時点と一致するとは限らない。そのため、絶対時点の ObservedAt を持つ遷移通知とマーカーを正確に時系列へ並べるには、マーカーにも同じ時計上の発生時点が必要になる。

## 決定

### Build は開始済みの実行者を返す

`AnimationStateMachineBuilder<TState, TContext>.Build(clock, initialState, context)` は、子の Timeline と内部の汎用制御を一括構築し、初期入場と再生開始を終えた `AnimationStateMachine<TState, TContext>` を返す。Composition の状態機械ノードにも `Build(clock, context)` を設け、同じ契約にする。Build 後に追加の実行者生成や Start を必要とせず、Update を呼び出せる。

定義だけを返す従来の `Build(initialState)` は削除し、定義への変換は内部 Compile に集約する。子タイムラインごとの Build は要求しない。同じ Builder／Composition ノードから複数回 Build すると、それぞれの時点のスナップショットと独立した実行者を生成し、初期入場を一度ずつ実行する。可変ノードへ構築結果を暗黙にキャッシュしない。

構築と開始を分ける用途では、既存のコンストラクターを使う。Composition ノード、Builder と初期状態、不変定義のいずれを受け取る場合も、コンストラクターは Stopped の実行者を作るだけで、初期入場・再生開始・値評価を行わない。開始時点で Start(context) を呼ぶ。

生成済み定義の共有と汎用機械への直接接続には、既存の `AnimationStateMachineDefinition` コンストラクターを維持する。消費側は汎用 Control と状態・Timeline の対応を渡して不変定義を作り、複数の実行者へ渡せる。定義を返すための新しい公開 Build メソッドは追加しない。

Build は必須引数と定義を検証してから、同じ Start(context) を一度実行する。null の clock／参照型 Context は ArgumentNullException、不整合な定義は既存の構築契約に従って失敗する。初期入場が例外を投げた場合は実行者を返さず、実行済みコールバックの外部副作用は巻き戻さない。Build 自体は値評価やマーカー配送を行わず、結果の出力は Update が担当する。

### 状態マーカーは絶対発生時刻を持つ

`AnimationStateEvent<TState>` に `OccurredAt: TimePoint` を追加する。状態識別子と既存の Occurrence は維持する。

- `Occurrence.Event.Time` は、その状態のタイムライン内の時刻。
- `Occurrence.LoopIndex` は、状態への入場から数えた外側の周番号。
- `Occurrence.UpdateOffset` は、状態機械の前回イベント走査基準からの時計上の時間幅。
- `OccurredAt` は、速度変更と一時停止を反映した、同じ時計上のマーカー通過時点。

OccurredAt は、内部で保持したイベント走査基準と補正済み UpdateOffset から算出する。消費側へ基準を推定させず、追加の時計読み出しも行わない。Pause 前の未通知マーカーを Resume 後に配送する場合も、OccurredAt は元の通過時点を保持する。UpdateOffset の基準は Pause 中の Update では進めない。

同じ時計を使うマーカーの OccurredAt と遷移の ObservedAt は直接比較できる。異なる時計の TimePoint 同士は比較しない。時刻の計算精度と Tick への丸めは [ADR-ANIMATION-0001](ANIMATION-0001-animation-system.md) に従う。複数コレクションの配送順と同時刻の優先順は消費側が決める。

### 既存判断の継承

上記以外は置換元の判断を引き継ぐ。汎用状態機械への依存方向、値適用とイベント配送を消費側に置く責務、定義のスナップショット、優先順位とトリガー消費、最大一遷移、超過時間を引き継がない切替、例外の部分完了契約を維持する。

Stop は汎用インスタンスを破棄して再生を停止し、各状態の再生資源は再利用する。Pause 中は既存の再生者で停止位置の値だけを評価し、マーカー・Release 終端の走査基準を保持する。これらは既存契約の具体化であり、別の再生規則を状態機械側へ追加しない。

## 公開 API の差分

比較元は main の `63f3e817fb214079e674e430b9e236d8047648f6`。判断対象の変更のみを示す。既存の AnimationStateMachine コンストラクター、Start、Update、AnimationStateMachineDefinition、AnimationEventOccurrence の公開シグネチャは維持する。

```diff
 using Lumyte.Core.Time;
 namespace Lumyte.Animation;

 public sealed class AnimationStateMachineBuilder<TState, TContext> where TState : notnull
 {
-    public AnimationStateMachineDefinition<TState, TContext> Build(TState initialState);
+    // 定義を内部構築し、初期入場・再生開始を終えた独立実行者を返す。
+    // null の clock／参照型 context は ArgumentNullException。
+    // 初期入場の例外は再送出し、実行者を返さない。
+    public AnimationStateMachine<TState, TContext> Build(
+        IMonotonicClock clock, TState initialState, TContext context);
 }

 public static partial class ComposeAnimation
 {
     public static partial class Definitions
     {
         public partial class StateMachine<TState, TContext> where TState : notnull
         {
+            // 子を含めて一括構築し、開始済みの実行者を返す。
+            // 引数・初期入場失敗の契約は Builder.Build と同じ。
+            public AnimationStateMachine<TState, TContext> Build(
+                IMonotonicClock clock, TContext context);
         }
     }
 }

-public readonly record struct AnimationStateEvent<TState>(
-    TState State, AnimationEventOccurrence Occurrence) where TState : notnull;
+// OccurredAt は、実行者と同じ時計上のマーカー通過時点。
+public readonly record struct AnimationStateEvent<TState>(
+    TState State, AnimationEventOccurrence Occurrence, TimePoint OccurredAt) where TState : notnull;
```

## 利用例

### Composition から直接実行者を作る

```csharp
using System.Collections.Generic;
using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var amount = AnimationChannel<float>.Create();
var pulse = new Tween<float>(0, 1, Duration.FromSeconds(0.2),
    AnimationInterpolators.Float, AnimationEasing.Linear);
ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(amount, pulse)];
ComposeAnimation.Definitions.StateMachine<string, bool> definition =
    StateMachine<string, bool>("Action")[
        State<string, bool>("Action",
            Timeline()[
                Sequence()[
                    Repeat(2)[Sequence()[forward, Reverse()[forward]]],
                    Marker("Finished")
                ]
            ])[
            Transition<string, bool>("Idle", onCompleted: true)
        ],
        State<string, bool>("Idle", Timeline()[Delay(Duration.FromSeconds(1))])
    ];

var clock = new ManualClock();
var machine = definition.Build(clock, true);
// Running、Action、位置 0。追加の Start は不要。
var output = new AnimationOutput();
var events = new List<AnimationStateEvent<string>>();
var transitions = new List<AnimationStateTransition<string>>();
clock.Advance(Duration.FromSeconds(1));
machine.Update(true, output, events, transitions);
// マーカーは開始から 0.8 秒、遷移の観測は 1 秒。
Duration deliveryDelay = transitions[0].ObservedAt - events[0].OccurredAt;
// deliveryDelay は 0.2 秒。時計を再取得して補正する必要はない。
```

Output とイベント・遷移の格納先は再利用し、次の更新前に消費側が Clear する。チャネル値の取得・適用と通知の配送も消費側で行う。[実行可能なサンプル](../../../samples/Lumyte.Animation.StateMachine.Sample/README.md)ではトリガーでアクションを開始する。

### Builder から直接実行者を作る

同じチャネルと値ソースを使い、Builder にも Composition の Timeline をそのまま渡せる。

```csharp
var builder = new AnimationStateMachineBuilder<string, bool>();
builder.AddState("Action", Timeline()[Sequence()[forward, Marker("Finished")]]);
builder.AddState("Idle", Timeline()[Delay(Duration.FromSeconds(1))]);
builder.AddTransition("Action", "Idle", onCompleted: true);
var machineFromBuilder = builder.Build(clock, "Action", true);
```

構築と開始を分ける場合は、次の入口を使う。

```csharp
var deferred = new AnimationStateMachine<string, bool>(clock, builder, "Action");
// ここでは Stopped。必要な時点で初期入場と再生を始める。
deferred.Start(true);
```

## 検討した代替案

### Build で定義だけを返し続ける

生成済み定義の共有には便利だが、通常利用で定義生成・実行者生成・開始の手順が分かれる。Build は利用可能な実行者まで返し、共有用途は直接作成した不変定義を使う。

### 消費側が Update 前後の時計から発生時刻を推定する

公開 API の追加は不要だが、SystemMonotonicClock の読み出し時刻は内部と一致せず、一時停止で保持した基準も復元しにくい。内部で確定しているマーカー通過時点を返す。

### イベント走査の基準時刻だけを公開する

UpdateOffset と組み合わせて絶対時刻を求められるが、消費側が更新・Pause・状態切替ごとの基準を管理する必要がある。配送対象のイベント自身に OccurredAt を持たせる。

## 結果と影響

- Build の戻り値をそのまま更新でき、通常利用での呼び出しをまとめられる。
- 構築と開始を分けるコンストラクター、および不変定義を共有する経路を維持できる。
- マーカーと遷移を、同じ時計上の絶対時点で並べられる。イベントごとに TimePoint 一つ分のデータが増える。
- Build のシグネチャ変更と AnimationStateEvent の位置引数追加は、比較元からの API 変更になる。

## 移行

`builder.Build(initialState)` で定義を作ってから実行者生成・Start を行っていたコードは、`builder.Build(clock, initialState, context)` にまとめる。Composition ノードから実行者生成・Start を行っていたコードは `definition.Build(clock, context)` にまとめられる。開始時点を別に決める既存のコンストラクター＋Start はそのまま使用できる。

定義を明示共有するコードは、汎用 Control と状態・Timeline の対応を `AnimationStateMachineDefinition` に渡す。AnimationStateEvent を自分で生成するコードでは、同じ時計上の OccurredAt を第三引数へ渡す。位置引数の分解も三要素へ変更する。既存の Occurrence プロパティを読むだけのコードは維持できる。

## 検証方針

- Builder と Composition の Build が Running の実行者を返し、初期入場が一度だけ実行されることを確認する。
- Build とコンストラクター＋Start が同じ値・イベント・遷移を返し、子の Timeline が一度だけ構築されることを確認する。
- 同じ構築ノードからの複数 Build の独立性、構築後のノード編集との分離、null と無効定義、初期入場の例外を確認する。
- 通常再生、速度変更、Pause／Resume、状態切替をまたいで OccurredAt が元の通過時点を保持することを確認する。
- Update が実時計を一度だけ読み、OccurredAt と ObservedAt を外部の時計読み出しなしで比較できることを確認する。
- 既存の型付き出力、Repeat／Reverse、Release、トリガー消費、最大一遷移と、ウォームアップ後の割り当てを回帰検証する。

## 実装・検証記録

2026-10-09、Linux / .NET 10 で実装を確認した。main `63f3e817fb214079e674e430b9e236d8047648f6` へリベース後、Animation 72 件と StateMachines 21 件が成功した。今回追加した回帰 32 件には、Build の開始・独立性・引数検証、状態をまたぐ絶対発生時刻、Pause 中の走査保持、Repeat／Loop／Reverse の周境界、離散値の時刻境界と極値補間を含む。既存のウォームアップ後の割り当てゼロの検証も成功した。

サンプル、NuGet パッケージからの両 Build API と OccurredAt の利用、対象プロジェクトのフォーマットと Markdown 検査を確認した。Windows／Browser の実行は未実施。

同日、状態別の候補配列を遷移先へ接続する最適化と、Output の有効スロットだけを走査する最適化を追加した。最適化後の Release テストは Animation 74 件と StateMachines 28 件が成功した。Linux x64 / .NET 10.0.12 の Release ビルドで、最適化前の `414442d` と 20 ケースを比較した。各実装を独立した 2 プロセスで実行し、各ケース 9 サンプルずつ測定した。状態数が多い候補探索と、使用済みチャネルの履歴が多い出力更新を改善し、全ケースの測定区間で Managed 割り当ては 0 B/op だった。一方、100 チャネルすべてが有効な単独再生では中央値が約 2〜3% 増えた。有効一覧を管理するコストがあるため、すべての構成で高速化するとは扱わない。

同じ比較元から内部の順位だけを ulong に置換した実験版でも、64bit に収まる 4 ケースを同条件で測定した。中央値の短縮は約 1〜5% だった。実験版は有効な巨大 Repeat の範囲を失い、製品版の代替にはならないため UInt128 を維持する。64bit を実際に超える登録範囲の中間位置・終端で、後登録の兄弟が優先されるよう、既存の回帰テストを強化した。測定条件・結果・制約は [PR #21](https://github.com/ikihiki/Lumyte/pull/21) に記録する。これらは単一環境の構築後の更新測定であり、構築時の時間・保持メモリ、イベント密集時、Windows／Browser の性能は未測定。

追加の Debug 検証は計 101 件成功、定常更新の割り当て検証 1 件が 72,000 bytes／1,000 Update で失敗した。比較元 `414442d` でも同じ失敗を再現しており、今回の最適化による回帰ではない。Release では同テストとベンチマークの割り当てゼロを確認した。

## 参考資料

- [ADR-ANIMATION-0001: アニメーションシステム](ANIMATION-0001-animation-system.md)
- [置換元 ADR-ANIMATION-0002: アニメーション用状態機械](ANIMATION-0002-animation-state-machine.md)
- [ADR-CORE-0001: 汎用状態機械](../core/CORE-0001-state-machine.md)
