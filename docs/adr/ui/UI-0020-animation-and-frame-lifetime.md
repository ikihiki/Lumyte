# ADR-UI-0020: Animationの値評価とUI引数・フレーム・表示寿命の接続

- 状態: 採用・実装済み（PR 16）
- 日付: 2026-10-28（採用: 2026-10-30）
- 対応: 実装PR 16、前提: 実装PR 12〜15・ANIMATION-0001〜0005

## 背景

既存のAnimationは単調時計、Tween、Curve、Timeline、再生と状態機械を提供するが、対象やsetterを保持しない。UIは[UI-0019](UI-0019-signals-and-reactive-state.md)でSignal引数の購読と寿命を自動管理する。この境界を使って、目標値の変化を補間し、ページやPopupが閉じた後に更新を残さない接続が必要である。

利用者に時計、frame callback、runtime、購読scopeを用意させず、通常の`using static Lumyte.UI.Compose;`から構築できるようにする。複数Window、Hosted fallback、非公開の遷移候補、Browserの描画休止、Headlessによる決定的な操作検証も同じ契約で扱う。

## 決定

### 責務と依存方向

UI本体へAnimationとCore.Timeの参照を追加し、目標値を補間する`UiAnimated<T>`と、既存Timelineを再生する`UiAnimationClip`を置く。通常のComposeへ静的なAnimate関数を追加する。別factory、Generator、Bind APIは作らない。UI-0001のAnimation非依存という提案はこの範囲で変更する。

```text
独立したモデルのSignal/Computed → UIの目標値監視
                                      ↓
UIが選ぶ単調時計 → AnimationのTween/Playback → 出力を一括でSignalへ反映
                                                        ↓
                        通常Compose引数 → UiSurface → layout → Record → Present
```

Animationの計算・再生契約は変更せず、AnimationからUI/Signalsへの参照を追加しない。SignalsもUI/Animationを参照しない。UIがproducerの接続、時計、出力の適用、callback配送を所有する。AnimationPlaybackやAnimationOutputを利用者へ渡す必要はない。

### 公開APIの追加差分

比較元はmain `4773a2b73a3a9a1a2b1d0b5e6c99364eaf070150`と先行UI提案のPR 15 head `b2096906abbec657330780f55efef213bb8cf0ee`。既存Animationの公開APIは追加・変更しない。以下の型はLumyte.UIへ置く。

```diff
+using Lumyte.Animation;
+using Lumyte.Core.Time;
+using Lumyte.Signals;
+using Lumyte.Graphics.Drawing2D;
+
+namespace Lumyte.UI
+{
+    public sealed class UiAnimated<T> : IReadSignal<T>
+    {
+        // factoryが生成する受動的な定義。読取だけでは再生や購読を開始しない。
+        internal UiAnimated();
+        public T Value { get; }
+        public T Peek();
+        // 同じproducerへの所属を維持した純粋な型変換。owner上で同期実行する。
+        public UiAnimated<TResult> Select<TResult>(Func<T, TResult> selector);
+    }
+    public readonly struct UiArgument<T>
+    {
+        // Tはpropertyと完全一致。FromSignalでも実体がUiAnimatedなら同じ接続を行う。
+        public static implicit operator UiArgument<T>(UiAnimated<T> source);
+    }
+    public static partial class Compose
+    {
+        // 初回はtargetの現在値。以後の変更を現在の表示値から補間する。
+        public static UiAnimated<T> Animate<T>(IReadSignal<T> target,
+            Duration duration, IAnimationInterpolator<T> interpolator,
+            AnimationEasing easing = AnimationEasing.EaseInOut,
+            Action? onCompleted = null);
+        // 不変Timelineを借用する受動的な再生定義。接続後に自動再生する。
+        public static UiAnimationClip Animate(AnimationTimeline timeline,
+            AnimationWrapMode wrapMode = AnimationWrapMode.Once,
+            Action<AnimationEventOccurrence>? onEvent = null,
+            Action? onCompleted = null);
+    }
+    public sealed class UiAnimationClip
+    {
+        internal UiAnimationClip();
+        // 同じclipの複数channelは一つのPlayback/Outputから評価する。
+        public UiAnimated<T> Value<T>(AnimationChannel<T> channel, T initialValue);
+        public AnimationPlaybackState State { get; }
+        public Duration Position { get; }
+        // 有限かつ非負。0は時間を進めず、連続frameを要求しない。
+        public double Speed { get; set; }
+        public void Play();
+        public void Pause();
+        public void Resume();
+        public void Seek(Duration position);
+        public void Stop();
+        public void Cancel();
+    }
+    public static class UiAnimationInterpolators
+    {
+        // 非null間はinner。nullを含む区間は終端で切替えるStep。
+        public static IAnimationInterpolator<T?> Nullable<T>(
+            IAnimationInterpolator<T> inner) where T : struct;
+        // sRGB入力をlinear premultipliedへ変換して補間し、Color2Dへ戻す。
+        public static IAnimationInterpolator<Color2D> Color { get; }
+    }
+    public sealed class UiSurface
+    {
+        // 高度なhost向け。clock/dispatcherは借用。既存constructorも維持する。
+        public UiSurface(Compose.Definitions.Widget root, IUiDispatcher dispatcher,
+            IMonotonicClock animationClock);
+        // owner Idleのみ。現在時刻で一回評価し、自動Signal配送まで進める。
+        public void AdvanceAnimations();
+        // 時間進行が必要なproducerが存在するか。再描画dirtyとは別である。
+        public bool HasRunningAnimations { get; }
+        // 新規target/control/接続による一回更新。停止中の連続frame要求と区別する。
+        public bool HasPendingAnimationUpdates { get; }
+        // host向け。既定true。時計・callback・再生を凍結し、値を保持する。
+        public void SetAnimationUpdatesEnabled(bool enabled);
+    }
+}
+namespace Lumyte.UI.Application
+{
+    public sealed record UiApplicationOptions
+    {
+        // nullならTimeProviderの単調timestampからhostが生成する。借用で解放しない。
+        public IMonotonicClock? AnimationClock { get; init; }
+    }
+}
```

型名の短縮は上記usingに従う。UiArgumentは既存のreadonly structへの追加宣言であり、既存memberを維持する。UiAnimatedはIReadSignalの読取契約を実装するが、Signal本体の新しい種類ではなくUI所有の表示値である。公開setter、Dispose、購読開始APIを持たない。

標準hostは生成と評価をowner queueへ直列化する。低水準APIの利用者は同時更新を避ける責任を持つが、生成・読取のためのthread登録やcreator-thread検査は要求しない。nullの必須引数はArgumentNullException、負duration・不正enum・非有限/負SpeedはArgumentOutOfRangeExceptionとする。interpolator、selectorは純粋かつ同期とし、Signalへの書込みやUIの変更を行わない。生成時のtarget読取・Select初期評価の例外は呼出し元へ返し、購読を残さない。通常callbackからのcontrolは次回のanimation phaseへ予約する。

Color2Dはstraight alphaのsRGB入力である。Color補間はdecode・premultiply後に補間し、alpha>0ならunpremultiply・encodeしてColor2Dへ戻す。alpha=0のRGBは0とし、端点は元値を返す。[Drawing2Dの色契約](../graphics/GRAPHICS-0018-2d-graphics-engine.md)に従って入力・結果を検証する。任意のcustom interpolator結果をUIが一律にclampしない。寸法に負値や非有限値が出た場合は通常propertyの検証で失敗する。オーバーシュートを使う場合は有効範囲を返すinterpolator/Selectを明示する。

### 静的構築と依存の識別

```csharp
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Lumyte.UI;
using static Lumyte.UI.Compose;

// ページのfieldなどに保持するモデル。
var widthSignal = Signal.Create<float?>(80f);
// IUiPage.Buildの内容。購読とframe更新はUIが管理する。
var animatedWidth = Animate(widthSignal, Duration.FromSeconds(0.2),
    UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));

return Panel()[
    Rectangle(width: animatedWidth, height: 24f),
    Button(width: 160f, height: 40f,
        onClick: () => widthSignal.Value = widthSignal.Value == 80f ? 160f : 80f)
];
```

通常のUiArgument記録にUiAnimatedのproducer情報を受動的に保持する。未接続treeは外部Effect、timer、dispatcher予約を持たない。UiSurfaceが接続時にproducerを一回だけ準備し、目標値監視と出力の内部Signalを接続する。利用者は追加のscopeを開かない。

同じproducerは一つのsurface内で複数property/Widgetへ共有でき、clipの複数channelも一回だけUpdateする。別surfaceへの同時接続は接続準備時にInvalidOperationExceptionとし、新しいtreeの所属を公開しない。複数Windowで同じモデルを表示する場合は同じtarget Signalから別々にAnimateを作る。これによりWindowごとの休止と寿命を独立させる。

Selectはproducerの所属を保ち、接続時に一緒に準備する。selectorは引数だけから計算し、別のSignalを読んで依存を追加しない。一般のSignal.ComputedでUiAnimatedを包んでもproducer所属は伝播しないため、UIへ渡す表示値の変換にはSelectを使う。UIはSignalsの依存グラフを探索しない。UiAnimatedを目標値とするアニメーションの連鎖は初期対象外とし、モデルのSignal/Computedを目標値にする。業務状態はアニメーションの中間値へ依存させない。

### 目標値の変更と中断

初回接続ではモデルの最新値をそのまま表示し、登場アニメーションを暗黙に開始しない。モデル変更は監視Effectで値をstagingするだけとし、Coreの評価中にPlayback、setterやcallbackを実行しない。次のowner Idleで一括して処理する。

再target時は、そのturnの時刻で既存Tweenを一度評価した現在値を始値、新しいtargetを終値としてTweenを作り直す。前回の目標値や前回描画値から飛ばさない。同じturnに複数回変わったtargetは最後の値へまとめ、`EqualityComparer<T>.Default`で同値なら再開始しない。durationは新しい遷移ごとの所要時間とし、速度の連続性は保証しない。

旧遷移の完了通知は世代で無効化する。最後の有効遷移が終値を反映した後にonCompletedを一回配送する。初回配置、同値更新、取消・取り外しでは完了通知しない。duration=0は次のIdleで終値と完了を一回反映し、連続frameを予約しない。レイアウト中に目標値が変わった場合はUI-0019のrevision検証で現在passを無効化する。

### 既存Timelineの再生

```csharp
var channel = AnimationChannel<float?>.Create();
var timeline = new AnimationTimelineBuilder();
timeline.Add(Duration.Zero,
    new Tween<float?>(80f, 160f, Duration.FromSeconds(0.3),
        UiAnimationInterpolators.Nullable(AnimationInterpolators.Float),
        AnimationEasing.EaseOut), channel);
timeline.AddEvent(Duration.FromSeconds(0.3), "Expanded");

var clip = Animate(timeline.Build(), onCompleted: () => completed.Value = true);
return Rectangle(width: clip.Value(channel, (float?)80f));
```

例のcompletedは呼出し側で作成した`Signal<bool>`とする。既存ComposeAnimationで構築したTimelineもそのまま渡せる。clipの既定開始意図はPlayで、少なくとも一つのValueがsurfaceへ接続した時に自動再生する。事前にStop/Cancel/Pauseを指定した場合はその意図を優先する。Valueは未接続時だけ追加でき、同じchannelを複数回指定する場合は型とinitialValueの一致を要求する。型付きchannelはAnimationの同一性を維持し、未寄与時にはその投影の直前値を保持する。clipを構築関数から使う場合はページのfieldなどへ保持し、同じclipのValueを再利用する。新しいAnimate(timeline)は独立した再生意図を持つ新しいclipであり、異なるclip同士の再生状態は自動照合しない。

Outputとイベント列は毎更新Clearして再使用する。Releaseは最終寄与を適用した後に出力を止めることであり、initialValueへの復元ではない。Hold、Repeat、Reverse、Loop、同channelの優先順位、0 Durationは[ANIMATION-0001](../animation/ANIMATION-0001-animation-system.md)と[ANIMATION-0005](../animation/ANIMATION-0005-zero-duration.md)に従う。Loopの0 Durationは初回一回だけ評価し、時間が進まないのでframeを継続要求しない。

Play/Pause/Resume/Seek/Stop/Cancel/Speedは既存Playbackと同じ意味で、owner Idleまたは通常のUI callbackから予約できる。未接続時は開始意図・位置だけを保持し、時計やtimerを作らない。接続済みでは次のanimation phaseへ順番に適用する。Seekは通過markerを配送せず移動先を再評価する。Stop/Cancelは表示値を復元しない。Pause/Speed=0/完了/停止は連続frame要求を解除する。Seek等の一回更新は別に要求する。Position/Stateは最後に適用済みの状態であり、control呼出しの同期完了を示さない。

markerを出力順に、続いてその更新の完了を、値の反映後にcallback引数へ配送する。callbackからのcontrolや目標値変更は次のanimation phaseへ回し、同じframeで再帰評価しない。切断・取消・navigation退役後のcallbackは世代で無効にする。イベントを伴う長時間のLoop/Repeatは出力量に比例したコストを持つ既存仕様を維持し、markerを黙って間引かない。

### 時計とframeの順序

標準hostは[UI-0016](UI-0016-ui-application-framework.md)の手順3のqueue/content処理の後、layout前にanimation phaseを追加する。同じturnで共有する単調時計を一回読み、全surfaceへ固定値を返すcaptured clockを使う。各producerはそこから非表示・停止期間を除くactive-time時計を持ち、Playbackの複数のNow参照でも同じ時点を使う。差分float秒の毎frame加算は行わない。

1. 有効な接続・control・最後の目標値を確定する。
2. producerごとにAnimationを一回評価し、出力とmarkerをstagingする。
3. 全producerが成功した場合だけ、一つのSignal.Batchで出力Signalへ反映する。通常のUI更新配送を進め、Core評価区間外で再構築結果を表示要素へ反映する。animationの専用表示値経路を最適化する場合も、接続済み宣言のsetterは呼ばず、表示要素へ適用して変更の種類を判定する。
4. marker/完了callbackを配送してから、最新値でlayout、IME/candidate位置更新、Record、提示を行う。callbackによる再targetは次回phaseで処理する。

phase開始時のproducer集合をsnapshotし、構築/callbackによる追加producerは次回phaseへ回す。各通知の直前に接続世代を再検査し、先行callbackがtreeを取り外した場合も後続通知を配送しない。AdvanceAnimationsの再入、Layout/Record/Dispatch中の呼出しはInvalidOperationException、surface破棄後はObjectDisposedExceptionとする。

同じturnの更新でUI-0019のSignalRevisionを進め、prepared layout/描画と入力geometryの整合検証に含める。model targetの更新も監視stampとして含め、まだ補間値を出していなくても候補viewの古さを検出する。layoutはFlutter型を維持し、専用更新経路がWidth/Heightの表示差分にはlayout無効化、Fill等の表示差分には描画だけの更新を要求する。宣言プロパティのsetterには無効化の副作用を持たせない。描画frameは可視領域全体を記録する。

HasRunningAnimationsがtrueの可視surfaceがある間、標準hostは既存FrameInterval/Browser rAFで次frameを要求する。target監視からのdispatcher workや入力callbackの処理後にはHasPendingAnimationUpdatesも確認し、停止状態からの一回更新を予約する。埋込みhostも同じ二つの状態を通常loopで確認する。無効中は両方falseとし、保留意図は再開時に一回更新へ戻す。評価結果が同値なら表示値の適用と無効化を省けるが、終了時刻やmarkerへ到達するための更新は継続する。全producer停止後はanimationだけを理由にframeを予約しない。GPU in-flight上限では中間画像を全て提出せず、次に提出できるframeへ最新値を使う。入力・IME・cleanupをanimationのために止めない。

AnimationClock=nullの標準hostはTimeProvider.GetTimestamp/GetElapsedTimeに基づきTimePointへ検証付き変換する。期限・Diagnostics timeoutは引き続きTimeProviderで測り、animationのPauseで期限を止めない。ゲームのSimulation clockは標準UI時計へ暗黙接続しない。HeadlessではManualClockを明示注入し、通常frame barrierで同じphaseを実行する。

| 環境 | frame駆動 | 休止と検証 |
| --- | --- | --- |
| Desktop（Windows/Linux） | 標準hostのFrameInterval | 非表示・最小化・描画不能時は時計を休止する |
| Browser | 既存の単一rAF予約 | document hidden/freeze中は進行を要求せず、復帰時に時計を再開する |
| Headless | 仮想提示のframe barrier | 注入ManualClockで中間値と通知順を検証する |
| ゲーム等への埋込み | 呼出し側のloop | AdvanceAnimationsとlayout/Record順序をhostが管理する |

UiSurface(root)等の既存constructorはsurface所有のSystemMonotonicClockを用いる。埋込みhostだけはAdvanceAnimationsを通常loopで一回呼ぶ。UpdateLayout/Record/Value読取は時間を進めず、隠れた二重評価をしない。時計の逆行・範囲超過はそのphaseを失敗させる。clockは借用し、frame/GPU完了時刻と同一とは解釈しない。

### 表示・navigation・複数Windowの寿命

producerの購読とframe要求はUI-0019の接続transactionへ含める。追加subtreeの準備に失敗したら、新規producer/watchだけを解除し、既存treeを保持する。最後の投影が取り外されたら目標値監視と予約を即座に解除する。再接続では新しい世代で、target方式は最新目標値から、clip方式は保持するcontrol意図と位置から開始する。自動的な完了callbackは追加しない。

SetAnimationUpdatesEnabled(false)は値と再生位置を保持し、active-time時計を凍結してcallbackとframe要求を停止する。モデルSignalや他surfaceは停止しない。無効中のtarget変更は再開時に最新値を読み、保持した表示値から新しい遷移を開始する。clipは休止時間を飛ばさず元の位置から再開する。Signal自動配送のPauseとanimationの休止は別の操作であり、標準hostが両方を管理する。

navigation候補は生成直後からanimationを停止して初期値だけで準備する。model監視はrevision比較に必要な記録を保持し、TryValidateCommitでUI-0019のSignalRevisionを検査する。commit内では評価・callbackを行わず、公開後のIdleから時計を開始する。旧viewのquiesceでは両方を停止し、rollbackでは凍結位置と最新targetを使ったlayout/再提示を確認してから入力gateを開く。commit済み旧viewは再開しない。GPU退役待ちの間もanimation workは増やさず、surface破棄で購読を全解除する。

非表示、最小化、PixelSize=0、Browser document hidden、graphics復旧中は関係するsurfaceを休止する。復帰時は休止中のmarkerを大量にcatch-upしない。Native PopupもHosted Popupも論理surfaceごとに同じ規則を使い、Hostedの提示rootが休止した場合はその全子surfaceも休止する。別Windowへの移動は旧接続の停止・解除、新接続の準備として扱い、古いWindow世代の仕事を配送しない。

アニメーション中も入力のWindow origin、capture、focusは既存契約を維持し、geometryは提示済みrevisionだけを公開する。IME候補の位置も確定layoutから更新し、アニメーションの未提示位置で入力先や候補anchorを進めない。TextDocumentの内容・selection・IME変換状態を一般のAnimateで書き換えない。

### エラー・自動操作・拡張境界

評価の途中失敗では未公開の出力・通知を全て破棄し、対象surfaceのanimationをfaultedとして停止する。標準hostは既存Failure/停止経路へ移り、callbackを自動再試行しない。単独surfaceではAdvanceAnimationsから例外を返し、以降の進行は拒否する。再開にはfaulted surfaceを破棄して新しいsurfaceへ接続する。Playback内部の進行、既に成功したSignal代入、構築やcallbackの外部副作用をrollbackできるとはしない。購読解除と予約取消は失敗時も行う。

[UI-0009](UI-0009-ui-automation.md)の操作は通常frameを進めながら対象の提示済みBoundsの安定を待つ。全アニメーションの停止は待機条件にせず、他Windowや無関係なLoopでClickを永久に止めない。対象自身が動き続ける場合は元の共通期限でTimeoutになる。[UI-0010](UI-0010-diagnostics-ui-automation.md)経由でも同じ通常入力・lease・期限を使い、遠隔操作だけanimationを止めない。

初期版はUI内部のPlaybackを既存AnimationDiagnosticRegistryへ公開しない。直接controlされると接続世代と表示休止を迂回するためである。UI専用のanimation inspect/control、OSのreduced-motion設定自動取得、spring/物理、暗黙layout遷移は後続の判断とする。表示・退出、共有要素とpageの視覚遷移は[UI-0021](UI-0021-connected-and-presence-transitions.md)で追加し、表示producerと異なる短期の寿命・予算・入力gateを定義する。0 Durationによる明示的な即時切替は使える。

既存AnimationStateMachineを使うアプリは、標準content.Advanceで既存Updateを一回実行し、取得したchannel値をSignal.BatchでモデルSignalへ適用できる。時計・状態機械・context・イベント配送の所有者はそのcontent/pageとし、UIは独立したモデルSignalとして受け取る。typed triggerや状態callbackをUiAnimationClipへ取り込む専用adapterは今回追加しない。この手動統合とUiAnimationClipで同じ実行者を二重更新しない。

### 表示・退出とconnectedへの拡張

[UI-0021](UI-0021-connected-and-presence-transitions.md)は実装PR 17の判断として、既存WidgetへのIsPresent/Transitions/SharedElementKey、表示・退出callback、navigationのVisualCompletionを追加する。新旧の視覚資源を保持するが、モデルSignalとWidgetを別treeへ移動しない。通常の値アニメーションは本ADRどおり休止/再開し、短いsnapshot演出は非表示や世代変更時に最終状態へ打ち切る。両者の寿命を混同しない。

## 検討した代替案

- 利用者がtimer/EffectからWidgetを毎frame更新する方式は、owner、表示休止、navigation退役の管理がページごとに分散するため採用しない。
- UI専用のTween/clockを新設すると既存Animationと境界・0 Duration・イベント規則が重複するため採用しない。
- 任意のUI.Animation packageへ隔離する方式は可能だが、通常Compose引数への型付き変換とsurfaceの接続寿命を追加adapterへ分ける必要がある。今回はAnimationを直接利用する範囲をUI本体へ置く。
- 全Windowで同じproducerを共有すると休止・再開・破棄の所有者が曖昧になるため、共有は同一surface内とし、モデルSignalだけをWindow間で共有する。
- 外側のSignal値そのものを補間途中の値へ書き戻す方式は、業務状態と表示値を混同し、複数viewの競合を生むため採用しない。

## 結果と影響

通常の静的構築のまま目標値のアニメーションと既存Timelineを使え、UIがframe、購読、表示寿命を管理できる。モデルSignal、Animationの値計算、UIの値適用という依存方向を維持する。一方、UI本体の依存はAnimation/Coreまで増え、複数surfaceで表示値を共有するにはproducerを分ける必要がある。寸法のアニメーションは毎frameのlayoutを伴い、描画だけの変化より費用が高い。実装の検証結果とCPU測定の範囲は末尾に記録する。

## 検証方針

- 通常using staticのliteral/Signal/UiAnimated/Select/nullableとcallback lambdaを、実際のGenerator経由のconsumerで検証する。
- ManualClockで中間値、再targetの連続性、同turnのcoalescing、0 Duration、終値、同値、速度0、Pause/Seek/Stop/Cancel、marker順・完了一回を確認する。
- Timelineの複数channelを一回評価し、Hold/Release/Loop/Repeat/Reverseと既存境界規則が変わらないことを確認する。
- 未接続treeの回収、接続失敗、最後のdetach、再接続、異owner/異surface拒否、取り外し後の仕事/通知が無効になることを確認する。
- 候補準備中のtarget変更、commit/quiesce/rollback、GPU退役、Popup・複数Native/Hosted、Browser hidden/復帰、device loss時に時計と購読が正しく休止することを確認する。
- layout/Record中の変更は古いrevisionを公開せず、提示済みgeometry・IME anchor・Automation actionabilityが一致することを確認する。
- HeadlessのManualClockでDiagnostics経由の操作を再現し、無関係なLoopが待機を妨げず、動く対象は期限で失敗することを確認する。
- 評価/変換/再構築/表示更新/callback例外、時計逆行、解除中の予約を検証する。定常評価の割り当て・layout費用と停止時のframe予約数を測定する。

## 関連ADR

- [ANIMATION-0003: 実行者とイベント時刻](../animation/ANIMATION-0003-animation-execution-and-event-time.md)
- [ANIMATION-0004: フォーマット非依存の値ソース](../animation/ANIMATION-0004-format-independent-value-sources.md)
- [UI-0017: navigationとページ寿命](UI-0017-navigation-and-page-lifetime.md)
- [SIGNALS-0001: UI非依存の状態管理](../signals/SIGNALS-0001-independent-signal-runtime.md)

## 実装と検証

`Compose.Animation.cs`、`UiAnimationProducer`、`UiTargetAnimation`、`UiAnimationClip`をUI本体へ追加する。通常のUiArgumentがproducerの所属も記録し、Signal接続transactionが購読と投影をまとめて保持する。型・key・slotが一致する再構築では、target・duration・interpolator・easingも一致する目標値producerの補間状態を引き継ぐ。構築関数内で毎回呼ぶNullable wrapperはinnerの同一性ごとに共有する。再構築と完了値の配送が重なる場合は、未配送の完了意図を新しいproducerへ引き継ぎ、次のphaseで一回だけ通知する。

標準hostは一turnに一度時計を読み、FrameIntervalの間隔で継続更新し、新しいtarget/controlには一回更新を許可する。既存の描画loop自体は静止画面でもFrameIntervalで動作し、このPRで変更しない。継続frame要求がfalseとは、animation評価に起因する継続更新が不要であることを表す。

Browserはvisibilitychange/freeze/resumeの世代を記録する。復帰したturnは全surfaceを一度休止させ、hidden中にowner turnが一度も回らなかった場合にもcatch-upを防ぐ。PopupはOpen状態とowner/presentationの可視性を満たす時だけ評価し、Closingでは即座に凍結する。

検証は`AnimationTests`で再target、全producer評価後の公開、再構築による引き継ぎ、通知の世代失効、非表示休止、clipのcontrols/marker/0 Duration、Colorのlinear補間を確認する。Headlessサンプルは標準hostへManualClockを渡し、中間/最終layoutと通知を確認する。BrowserサンプルのGPU PNG検証は同じ時計で中間/最終画像とfreeze/resumeを確認する。

実装時の回帰検証は全体2,070件成功・失敗0件（環境依存75件skip）、追加したconstructor検証を含むUI220件、Windowing40件、Application29件成功である。BrowserはChromium/SwiftShader、CPU throttle 6、同一frameのPNG取得2回に加え、中間値/最終値の実GPU画素とfreeze/resumeを確認した。停止後の確認処理は追加のheap確保0 bytesで、補間評価を行わない。動作中のtree再構築の確保量と限界は[再現用CPU測定](../../benchmarks/ui-animation.md)に記録する。

NativeAOTはLinux x64のHeadlessサンプルをILLink/ILC warnings-as-errorsでpublishし、生成ELFを実行した。標準hostによる目標値とTimelineの同時更新、中間/最終layout、marker一回、完了一回、終了を確認している。NuGet packの依存にはAnimation/Core/Signalsを含め、基本ApplicationからNavigationへの依存がない既存の境界を維持する。
