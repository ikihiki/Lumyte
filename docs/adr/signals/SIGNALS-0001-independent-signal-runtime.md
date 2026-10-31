# ADR-SIGNALS-0001: UIに依存しないシグナル状態グラフ

- 状態: 採用（main向け独立package）
- 日付: 2026-10-30
- 対応: UIから独立したCore機能

## 背景

状態値、導出値、副作用の依存関係をUI外のモデルでも利用したい。利用者が実行機関を作り、値やscopeをそこへ所属させ、手動で更新を進める方式は通常の状態記述へ管理手順を持ち込む。Signalを直接作成し、依存追跡・batch・更新配送は内部で管理する。

Preact Signals、Solid、Vueでは、通常の状態作成はsignal/createSignal/ref等で行い、必要な場合だけEffectの解除やroot/effect scopeを管理する。状態の作成、購読の寿命、配送先を分離し、UIとの連携もその境界へ置く。

## 決定

### 配置と通常API

.NET 10の`Lumyte.Signals`を`src/Core/Lumyte.Signals`へ追加する。namespaceとNuGet名も同名とし、BCLだけへ依存する。UI、Composition、Window、Graphics、DI、Diagnosticsを参照しない。ResourcesのResourceStreamSignalは資源ストリームの通知であり、別の型とする。

通常利用はSignal.Create、Signal.Computed、Signal.Effect、Signal.Batchで完結する。公開SignalRuntime、runtimeの引数/property、runtimeを作るcontext拡張、Flush/Stop/ObservePending等のpump操作は設けない。内部の依存追跡・batch queue・評価contextを利用者へ渡さない。

### 公開API

比較元はmain `718c5d5ead778ae6bfb40446ba4ba752ecca9a83`。同revisionにLumyte.Signalsはなく、以下は新規APIである。

```diff
+namespace Lumyte.Signals
+{
+    public interface IReadSignal<out T>
+    {
+        // 同期評価中の読出しを依存として追跡する。
+        T Value { get; }
+        // 依存を追加せず読む。Computedの必要な再評価は行う。
+        T Peek();
+    }
+    public static partial class Signal
+    {
+        public static Signal<T> Create<T>(T initialValue, IEqualityComparer<T>? comparer = null);
+        public static Computed<T> Computed<T>(Func<T> compute, IEqualityComparer<T>? comparer = null);
+        // 初回は同期実行。通常の再実行は自動。scheduler省略時は変更の外側で同期実行。
+        public static SignalEffect Effect(Action effect, ISignalScheduler? scheduler = null,
+            Action<Exception>? onError = null);
+        // ネスト可。finallyでbatchを閉じる。成功済み代入のrollbackはしない。
+        public static void Batch(Action update);
+        public static T Untracked<T>(Func<T> read);
+        public static T CaptureReads<T>(Func<T> build, out SignalReadSet reads);
+    }
+    public sealed class SignalReadSet
+    {
+        public bool IsEmpty { get; }
+        public bool IsCurrent { get; }
+        public void Read();
+    }
+    public sealed class Signal<T> : IReadSignal<T>
+    {
+        public T Value { get; set; }
+        public T Peek();
+    }
+    public sealed class Computed<T> : IReadSignal<T>
+    {
+        public T Value { get; }
+        public T Peek();
+    }
+    public sealed class SignalEffect : IDisposable
+    {
+        public Exception? Failure { get; }
+        // 上流の非同値変更のstamp。他Effectとも比較可能。
+        // Pause中も変わり、巻き戻さない。
+        public ulong Revision { get; }
+        public bool IsPending { get; }
+        public void Pause();
+        // 成功しているEffectの一時停止を解除し、未反映分を自動配送する。
+        public void Resume();
+        // 直列化・冪等。予約を失効させ、依存とcallbackを解除する。
+        public void Dispose();
+    }
+    public sealed class SignalScope : IDisposable
+    {
+        // 任意の購読グループ。Signal/Computed自体を所有しない。
+        public SignalScope();
+        // 同期区間で生成したEffectを登録する。ネストでは最内scopeへ所属する。
+        public void Run(Action setup);
+        // 登録Effectを逆順に解除する。外へ保持したSignal値は有効なまま。
+        public void Dispose();
+    }
+    public interface ISignalScheduler
+    {
+        // host/adapter向けの任意接続。仕事をhostの実行先へ一回配送する。
+        // 同期実行も可。再入を避けるため、coreは評価区間を抜けてから呼ぶ。
+        void Schedule(Action work);
+    }
+}
```

Signal/Computed/Effectは静的factoryから生成し、new SignalScopeも任意である。必須callbackのnullはArgumentNullException、comparer/scheduler/onError省略は既定動作とする。comparerは純粋・同期で、既定はEqualityComparerの標準比較である。同値代入では変更を配送しない。比較失敗時は代入前の値を保つ。

値はコピーされない。mutableなcollectionの内部変更は追跡せず、immutableな値の再代入を基本とする。型の暗黙推測、objectを介した変換、reflectionを使わない。

### host向けの受動的な依存記録

`Signal.CaptureReads<T>(Func<T> build, out SignalReadSet reads)`は同期読出し集合を記録して結果を返す。`SignalReadSet.IsEmpty`、`IsCurrent`、`Read()`を公開する。Readは記録した依存を現在のEffectへ再登録し、IsCurrentは記録時revisionとの一致を調べる。記録自体には能動購読がなく、modelを破棄しない。hostは構築処理をEffect評価の外で実行し、その後に読出し集合だけをEffectへ接続できる。構築処理で業務scopeを初期化しても、Computed/Effectの評価中の書込み禁止は緩めない。

### ownerと自動更新

生成threadへの固定は設けない。graphの読出し・変更・同期評価は内部lockで直列化し、同じSignalを異なるthreadから順次利用できる。UIの構築場所とSignalモデルの生成場所を一致させる必要はない。短い同期callback/comparerを前提とし、評価中に別threadの処理完了を待つ使い方は契約外とする。Batchはcallerの同期区間の配送集約であり、他threadを含むtransactionではない。UI資源への反映はhostのschedulerが担当する。

内部の評価stackとbatch queueはthreadごとの一時contextとして実装する。UI threadやSynchronizationContextを暗黙取得せず、依存グラフを列挙保持するglobal registryも設けない。Signalを作るためにサービス登録、host起動、current scopeの設定を要求しない。

Signalへの代入は値とdirtyを同期確定し、最外の変更処理またはBatch終了後にEffectを自動配送する。scheduler未指定のEffectは変更を行ったcaller上で同期実行する。Batchは既存Effectの再実行をまとめ、内部のValue読出しには途中値が見える。Effectを明示作成した時の初回実行はBatch内でも同期で行い、scope登録と依存確立を完了する。

任意schedulerはhost/adapterだけが指定すればよい。内部のper-Effect予約・世代で更新を一件に集約する。schedulerへ渡す仕事は停止済みEffectの世代を確認し、遅い配送をno-opにする。host adapterは配送先を実装側で接続し、通常利用者へschedulerやpumpの設定を要求しない。schedulerは全グラフの処理権や寿命を所有しない。

### 計算と依存追跡

Valueの同期読出しだけを追跡し、Peek/Untrackedは外側への依存登録を抑制する。Computed自身の内部依存まで消すことはしない。thread-localな評価stackはfinallyで復元し、awaitへ伝播させない。

Computedはlazyで、依存を先に評価して一つの結果を返す。条件分岐の依存集合は成功時に交換する。diamondでも同じ変更に対する新旧値を混在させない。観測されていないComputedは読出し時に再計算してよく、最後の観測が外れたら能動購読を解除する。computed delegateの所有する通常の参照は保持する。

Computed/comparerは副作用なしとする。Computed/Effect/comparer内のSignal書込み、Effect新規作成、batch/scope操作や配送再入は拒否する。Effectは短い同期処理に限定し、async delegate/async voidは契約外とする。状態変更はコマンドやイベントcallbackで行い、再帰Computedは評価stackで検出する。

Revisionは全nodeで共有する単調な変更stampであり、別threadから作ったEffect間でも比較できる。上流変更はPause済み・既にdirtyのEffectへもRevisionとして伝播する。一回の書込みにつき依存走査を重複除去し、準備後の変更をdirtyのまま見落とさない。Revisionのoverflowはwrapせず、当該変更前にOverflowExceptionで失敗する。Computed同値時にもEffectを一回再実行してよく、利用側は出力比較で副作用を抑制できる。

### 寿命と失敗

Signal/ComputedはUIや購読scopeに所有されず、通常の参照とGCで保持する。購読解除後もアプリが持つ値は利用できる。Effectと非同期adapterは明示的な解除責任を持ち、戻ったhandleのDisposeまたは任意のSignalScopeで解放する。

SignalScope.Runはその同期区間で作成したEffectと非同期adapterを登録し、成功/例外のいずれでもfinallyで直前の捕捉contextへ戻す。Run本体の失敗は成功済みEffectをrollbackせず、scopeが解除責任を保持する。asyncへの伝播、全Signalの回収、schedulerの停止は行わない。ネストでは最内scopeがEffectと非同期adapterを所有し、別途作ったscope自体の所有権は自動移譲しない。Disposeは登録Effectのcallback・依存を解除し、失効済みhandleへの再Disposeをno-opにする。UIの購読を解除しても共有モデルを停止しない。Run中の同scope.Disposeは変更前に拒否し、解放後のRun/Pause/ResumeはObjectDisposedExceptionとする。

初回Effectが失敗したら部分購読を解除してfactoryから例外を返し、無効なhandleを返さない。後続Effectの失敗はFailureに保存して当該Effectを停止し、他のEffectを壊さない。onError省略の既定同期配送では、当該変更の残る配送を清掃してから例外をcallerへ返す。onError指定時は配送先上で一回通知し、正常に通知できれば処理済みとする。通知自体が失敗した場合もFailureへ集約し、同期配送ではその失敗をcallerへ返す。任意schedulerによる後続実行は例外を仕事の外へ漏らさず保持し、hostがFailure/onErrorで観測する。配送自体の拒否も失敗として保持する。配送先の選択は指定schedulerの契約に従う。

失敗したEffectはResumeで暗黙復旧させない。必要なら解除して再作成する。Computed直接読出しの例外はcallerへ返し、旧値を正常値として返さない。失敗時は直前と今回読んだ依存の和集合を一時保持し、次の成功で確定する。batchの例外でも成功済み代入は戻さず、finallyで既定Effectの配送とschedulerへの予約を終える。非同期schedulerの仕事完了はBatchの完了条件に含めない。処理本体と配送の両方が失敗した場合は両例外を保持する。

### 単独での利用例

```csharp
using Lumyte.Signals;

var count = Signal.Create(0);
var doubled = Signal.Computed(() => count.Value * 2);
using var effect = Signal.Effect(() => Console.WriteLine(doubled.Value));
Signal.Batch(() => { count.Value = 1; count.Value = 2; });
```

初回に0、batch後に4を自動出力する。UI、context、実行機関の作成や手動pumpは不要である。

## 検討した代替案

- 公開runtimeの生成とFlushを必須にすると内部の管理手順が通常コードへ出る。nodeを直接作り、内部で自動配送する。
- 生成thread固定はモデルを再利用するたびに制約を意識させる。内部で同期を直列化し、UIへの配送先だけhostが選ぶ。
- UI contextにSignalを所属させると共有モデルの寿命がviewに従属する。scopeは購読と非同期adapterの解除を担当する。

## 結果と影響

状態・導出値・Effectを直接記述でき、UIの有無と独立して使える。通常利用者へruntime、scheduler、手動更新を要求せず、購読の寿命だけを必要に応じて管理する。

内部の自動配送と同期規則を実装する必要がある。非同期Computed、collection差分、state persistence、性能最適化は後続設計とする。非同期関数の明示変換は[SIGNALS-0002](SIGNALS-0002-async-factory-conversion.md)へ分離し、UIへの適用は利用側で設計する。

## 検証方針

- UI、Composition、DIを参照しないconsole consumerで、runtimeやFlushなしに状態・導出値・Effectが動くことを確認する。
- 同値代入、mutable値、動的依存、diamond、Peek/Untracked、循環、thread間の順次更新、再入、追跡stack復元を確認する。
- 初回/後続Effect、Batch内の新規Effect、ネストbatch、途中例外、配送の集約、複数schedulerと配送拒否を検証する。
- Pause中や既にdirtyのRevision、失敗の観測・通知例外、Dispose後の遅延仕事を確認する。
- 任意scopeの捕捉と解除、解除後の共有Signal、未観測Computedの購読解除とGC、AOT/trimmingを確認する。

## 参考資料

- [Preact Signals Core](https://github.com/preactjs/signals/blob/main/packages/core/README.md)
- [Solid createRoot](https://docs.solidjs.com/reference/reactive-utilities/create-root)
- [Vue effectScope](https://vuejs.org/api/reactivity-advanced.html#effectscope)

## 実装記録

2026-10-31、main向け独立PRで[Core package](../../../src/Core/Lumyte.Signals/README.md)と[独立console consumer](../../../samples/Lumyte.Signals.Sample/README.md)を追加した。単体テスト23件（同期グラフ8件、非同期変換15件）でdiamond・動的依存・未観測Computed・batch・Peek・受動read set・schedulerの集約と失効・Pause中のrevision・失敗・scope・thread間の順次更新を確認した。NuGet packの依存groupは空であり、UI/Compositionへの実行時依存を持たない。linux-x64のNative AOTをILLink/Ilc warning-as-errorでpublishし、生成した実行ファイルでbatchと購読解放を確認した。

### PR #99レビュー対応

Computedの読出しは例外の場合も外側の依存として記録する。Effectが例外を捕捉して正常終了しても、入力復旧時の再評価を継続する。未購読Computedの旧依存確認は最初の変更検知で打ち切り、本体を再評価して現行の分岐だけを読む。

初回/後続のComputed例外捕捉と、Value/Peekで不要になった旧分岐の例外を避ける回帰テストを追加した。
