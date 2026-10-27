# ADR-ANIMATION-0006: Animation と状態機械の結果・Context 契約の明確化

- 状態: 採用
- 日付: 2026-10-26

## 背景

Animation の `TryGet` は参照型のチャンネルで値がなければ null を返すが、その条件を nullable 注釈に表していない。また二つの `Update` は bool を返すものの、再生者では今回の完了、状態機械では今回の遷移を表し、XML ドキュメントから違いを判断できない。汎用状態機械では単一入力だけが保持 Context を省略でき、複数入力に切り替える際に呼び出し方が変わる。

## 決定

既存の処理結果・ライフサイクルを保ち、型注釈、説明、転送オーバーロードで呼び出し契約を明確にする。[ADR-ANIMATION-0001](ANIMATION-0001-animation-system.md)、[ADR-ANIMATION-0003](ANIMATION-0003-animation-execution-and-event-time.md)、[ADR-CORE-0001](../core/CORE-0001-state-machine.md) を補足する。

### 公開 API の差分

比較元は main の `16d856a`。以下は今回の変更対象だけを示す。

```diff
 namespace Lumyte.Animation
 {
     public sealed class AnimationOutput
     {
-        public bool TryGet<T>(AnimationChannel<T> channel, out T value);
+        // 寄与がない場合は false と default(T)。成功時は T の null 許容性に従う。
+        public bool TryGet<T>(AnimationChannel<T> channel,
+            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value);
     }
     public sealed class AnimationPlayback
     {
         // 今回の呼び出しで Once 再生が完了した場合だけ true。
         // false でも値やイベントを追記する場合がある。
         public bool Update(AnimationOutput output, ICollection<AnimationEventOccurrence> events);
     }
     public sealed class AnimationStateMachine<TState, TContext> where TState : notnull
     {
         // 今回の呼び出しで遷移した場合だけ true。自己遷移も含む。
         // false でも値やイベントを追記する場合がある。
         public bool Update(TContext context, AnimationOutput output,
             ICollection<AnimationStateEvent<TState>> events,
             ICollection<AnimationStateTransition<TState>> transitions);
     }
 }
 namespace Lumyte.StateMachines
 {
     public sealed class StateMachineInstance<TContext, TTrigger>
     {
+        // 保持 Context で評価。コールバック、優先順位、例外は明示 Context 版と同じ。
+        public bool FireAny(IReadOnlyList<TTrigger> triggers);
+        public bool CanFireAny(IReadOnlyList<TTrigger> triggers);
         public bool FireAny(IReadOnlyList<TTrigger> triggers, TContext context);
         public bool CanFireAny(IReadOnlyList<TTrigger> triggers, TContext context);
     }
 }
```

`TryGet` の成功は非 null の保証ではなく、チャンネルの型 `T` が表す値の存在を意味する。`AnimationChannel<string?>` は成功時にも null を格納できる。失敗時の既存の default 値や例外条件は変更しない。

Context 省略版は保持 Context を使う明示版へ転送する。明示版は今回だけの Context を使用し、保持 Context を置換しない。null の入力集合は ArgumentNullException、空集合や不成立入力は false とする。`CanFireAny` はガードを評価するが遷移アクションを実行しない。

`Update` の bool は成功・失敗や出力の有無を表さない。README と XML ドキュメントではそれぞれ `completed`／`transitioned` の意味を説明し、出力は戻り値にかかわらず消費側が確認する。コレクションのクリア・適用は引き続き消費側が担当する。

## 検討した代替案

### Update に新しい結果型を導入する

プロパティ名で意味を表せるが、現状は各メソッドが一つの状態変化を報告しており、既存呼び出しの変更に見合う情報は増えない。今回は bool の意味を明記する。

### 複数入力では必ず Context を明示する

既存の実装だけで利用できるが、単一入力と複数入力の切り替えに不要な変更が必要になる。保持 Context を使う選択肢を双方に設ける。

## 結果と影響

- `TryGet` 失敗後に参照型の値を確認せず使用していたコードには nullable 警告が出る。戻り値を確認して使用することで解消できる。
- Context 省略のオーバーロードは追加のみで、既存の明示 Context 版と実行規則を共有する。
- `Update` のシグネチャと実行時動作は変更しない。異なる bool の意味は IntelliSense と利用例で確認できる。

## 検証方針

既存の Animation と StateMachines のテストを実行する。複数入力の既存テストで省略版を使用し、保持 Context と明示 Context の使い分け、優先順位と入力重複の扱いを確認する。参照型出力の既存テストと警告をエラーとするビルドで nullable 注釈の影響を確認する。

2026-10-26、Linux / .NET SDK 10.0.401 の Release 構成で Animation 145 件と StateMachines 28 件が成功した。状態機械の実行サンプルも完了し、マーカーの発生と遷移の観測が 0.2 秒異なることを確認した。
