# Lumyte.StateMachines

型付き Context・Trigger、複数ガード、入場・退出アクション、遷移エフェクト、優先順位、通知・診断を持つ同期状態機械。

```csharp
using Lumyte.StateMachines;

var idle = new State<Context>("Idle");
var ready = new State<Context>("Ready").OnEnter(context => context.Count++);
var builder = new StateMachineBuilder<Context, Trigger>(idle);
builder.AddTransition(new Transition<Context, Trigger>(idle, ready, Trigger.Activate)
    .When(context => context.Enabled).WithPriority(10));
var machine = builder.Build(context);
if (machine.CanFire(Trigger.Activate))
{
    machine.Fire(Trigger.Activate);
}
```

`Build(context)` は定義を確定し、初期入場を実行した実行者を返す。`BuildDefinition()` は共有する不変定義だけが必要な場合に使う。ガードは優先順位順に調べ、同順位は登録順。成立した一遷移について、退出 → エフェクト → 状態変更 → 入場 → 通知の順で実行する。

`FireAny` は複数入力から最大一遷移を選ぶ。トリガーを保留せず、未知・不成立の入力は false。定義確定後は状態と遷移の設定を変更できない。例外時にコールバックの副作用は巻き戻さない。実行者は一スレッドだけで操作する。

`Fire`／`CanFire` と `FireAny`／`CanFireAny` は、Context を省略すると生成時の保持 Context を使う。明示した Context はその呼び出しだけで使い、保持 Context を置換しない。

```csharp
bool transitioned = machine.FireAny([Trigger.Activate]);
bool canTransition = machine.CanFireAny([Trigger.Activate]);
```

`FireAny` の true は自己遷移を含む一遷移の実行、`CanFireAny` の true は実行可能な候補の存在を表す。[結果と Context の契約](../../../docs/adr/animation/ANIMATION-0006-api-result-contracts.md)も参照。

[設計と契約](../../../docs/adr/core/CORE-0001-state-machine.md)と[実行サンプル](../../../samples/Lumyte.StateMachines.Sample/README.md)を参照。
