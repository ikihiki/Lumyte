# ADR-ANIMATION-0005: アニメーション全体で 0 Duration を許可する

- 状態: 採用
- 日付: 2026-10-11
- 置換対象: [ADR-ANIMATION-0001](ANIMATION-0001-animation-system.md) の正の Duration 制約

## 背景

即時の値変更、時刻 0 のイベント、空のタイムライン、glTF の単一キーなどは時間を消費しない。これらへ架空の 1 Tick を与えると、順次配置の時刻、完了判定、外部形式の時刻を変えてしまう。一方、既存実装は値ソースと最終タイムラインに正の長さを要求し、Loop、Repeat、Reverse の一部で長さによる除算を行っていた。

## 決定

値ソース、Timeline、Composition の Delay／Track／Sequence／Parallel、Repeat／Reverse、状態機械の各状態で 0 Duration を許可する。負の長さは引き続き拒否する。

- 0 Duration の Tween は時刻 0 で終値を返す。Curve と HermiteCurve は時刻 0 の単一キーを返す。
- Once は Play 後の最初の Update で時刻 0 を一度評価し、時刻 0 のマーカーと Release 終端を一度出力して Completed になる。Hold は Completed 後も値を出力し、Release は以後寄与しない。
- Loop は位置 0 のまま Playing を保つ。時間が進んでも無限個の周回が発生したとは解釈せず、Play ごとに時刻 0 のマーカーを一度だけ出力する。
- Repeat(count) は 0 Duration の子を count 回順に評価したものとして、マーカーを count 回出力する。同じチャネルの値は最後の反復を優先する。Reverse は同じ瞬間を反転しても結果と発生時刻を変えない。
- 空の Timeline は有効で、値もイベントも持たない即時完了として扱う。
- 0 Duration の状態はアニメーション状態機械へ登録できる。最初の更新後は完了条件を満たし、自動遷移の候補になれる。遷移先が 0 Duration の場合、その開始マーカーを遷移を観測した更新で出力する。

瞬時項目は、正の長さの親の始端・終端へ配置しても同じ通過規則を使う。Reverse を重ねても初回の境界通過を失わず、Repeat と組み合わせた Release の終端評価と Hold の通常評価は同じ反復順の優先度を使う。内部の終端走査は Int128 の局所座標で境界の外側も表し、時刻を反転する前のクランプで通過情報を消さない。公開 Duration の範囲は変えない。

遷移先の瞬時マーカーも [ADR-ANIMATION-0003](ANIMATION-0003-animation-execution-and-event-time.md) のイベント時刻契約に従う。OccurredAt は遷移時点、UpdateOffset は状態機械の前回イベント走査基準からの差とする。Pause 中の更新はその基準を進めない。

## 公開 API の差分

比較元: main `f3f4f670d48ffbd9f663f9ec737e462a280cd5ca`。シグネチャの追加・削除はなく、次の既存 API の事前条件と結果を変更する。

```diff
 public sealed class Tween<T> : IAnimationSource<T>
 {
-    // duration は正。時刻 0 は from、時刻 Duration は to。
+    // duration は非負。0 Duration の時刻 0 は to。
     public Tween(T from, T to, Duration duration,
         IAnimationInterpolator<T> interpolator, AnimationEasing easing);
 }
 public sealed class AnimationCurve<T> : IAnimationSource<T>
 {
-    // duration は正。
+    // duration は非負。0 Duration では時刻 0 の単一キーを要求する。
     public AnimationCurve(Duration duration, IReadOnlyList<AnimationKey<T>> keys,
         IAnimationInterpolator<T> interpolator);
 }
 public sealed class AnimationHermiteCurve<T> : IAnimationSource<T>
 {
-    // duration は正。
+    // duration は非負。0 Duration では時刻 0 の単一キーを要求する。
     public AnimationHermiteCurve(Duration duration,
         IReadOnlyList<AnimationHermiteKey<T>> keys,
         IAnimationHermiteInterpolator<T> interpolator);
 }
 public sealed class AnimationTimelineBuilder
 {
-    // source.Duration と明示 duration は正。
+    // source.Duration と明示 duration は非負。空の Build も成功する。
     public void Add<T>(Duration start, IAnimationSource<T> source,
         AnimationChannel<T> channel, AnimationFillMode fill = AnimationFillMode.Hold);
     public void SetDuration(Duration duration);
     public AnimationTimeline Build();
 }
```

## 例

```csharp
var visibility = AnimationChannel<bool>.Create();
var instant = new Tween<bool>(false, true, Duration.Zero,
    AnimationInterpolators.Step<bool>(), AnimationEasing.Linear);
var builder = new AnimationTimelineBuilder();
builder.Add(Duration.Zero, instant, visibility, AnimationFillMode.Release);
builder.AddEvent(Duration.Zero, "Shown");

var playback = new AnimationPlayback(clock, builder.Build());
playback.Play();
playback.Update(output, events); // true。true と Shown をこの更新で一度出力する。
```

## 検討した代替案

- 最小長を 1 Tick に丸める案は、外部形式の時刻と Sequence の長さを変えるため採用しない。
- 0 Duration を値ソースだけに許可し Timeline で拒否する案は、即時値とイベントを組み合わせられず、Composition と状態機械で別規則になるため採用しない。
- Loop の経過時間から無限周回を生成する案は出力量が定義できないため採用しない。

## 結果と影響

即時変更と空定義を特別なダミー時間なしで表現できる。実装は 0 除算を避け、初回評価済みかを再生状態で管理する必要がある。0 Duration の Repeat が持つイベント数は count に比例する。

## 検証方針

0 Duration の標準ソース、Builder と Composition、Once／Loop、Hold／Release、Repeat／Reverse、状態機械登録をテストする。正の Duration の既存境界・イベント時刻・割り当て特性が変わらないことも既存テストで確認する。

## 実装・検証記録

2026-10-11、レビューで確認した Reverse の瞬時 Release 欠落、Repeat と Reverse の優先順位、遷移先マーカーの UpdateOffset を修正した。多重 Reverse、最大 Tick、親内の配置、Hold／Release の登録順、Pause と Once／Loop の遷移を回帰テストへ追加し、Animation 145 件と Mathematics 33 件の Release テストが成功した。後者には有限の大きな外挿を通じた定数保持も含む。対象の C# 整形、Markdown 53 ファイル、差分の空白検査も成功した。

比較元 `2cc91d4` と修正後の独立 DLL を交互に 2 プロセスずつ実行した。.NET 10.0.12 / Linux x64、Release、tiered compilation 無効、200 ms のウォームアップ後に約 50 ms のバッチを各 7 サンプル測定し、プロセスごとの中央値を平均した。全 8 ケースで Managed 割り当ては 0 B/op だった。Hold は 100.63 → 101.60 ns、Release は 177.44 → 185.19 ns、Release＋Reverse は 175.58 → 190.24 ns、Release＋Repeat(8) は 215.37 → 229.85 ns、Release＋Repeat(8)＋Reverse は 243.08 → 231.64 ns だった。境界情報の保持と内部座標拡張には処理時間のコストがあり、一律の高速化とは扱わない。単一環境のマイクロベンチマークであり、実際のフレーム時間を示さない。
