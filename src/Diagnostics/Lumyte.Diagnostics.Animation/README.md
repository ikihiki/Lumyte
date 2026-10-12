# Lumyte.Diagnostics.Animation

`Lumyte.Animation`の再生実行者を、既存の診断Operationへ明示的に公開する接続パッケージです。
Animation本体はDiagnostics、DI、通信へ依存しません。

```csharp
services.AddDiagnosticExecutionPoint<BeforeAnimationUpdate>("animation.before-update");
services.AddAnimationDiagnostics<BeforeAnimationUpdate>();

AnimationDiagnosticRegistry registry = scope.ServiceProvider.GetRequiredService<AnimationDiagnosticRegistry>();
AnimationDiagnosticRegistration registration = registry.Register("character.main", playback);
```

登録IDは1～128文字の小文字ASCII識別子で、スコープ内で一意です。最大128対象を登録できます。
返された`AnimationDiagnosticRegistration`を破棄すると、その登録だけが解除されます。
レジストリと登録対象は、診断PumpおよびAnimation更新と同じ所有スレッドで使用してください。

サブシステムIDは`animation`です。`list`と`read`はObserve権限、`play`、`pause`、`stop`、`seek`、`set-speed`、`set-trigger`、`clear-triggers`はEdit権限を要求します。
操作は診断実行ポイントで同期実行され、通信スレッドからAnimationへ直接アクセスしません。
対応する診断Serverでは、`Animation`ページから公開対象の選択、scalar状態の更新、再生・Seek・速度・Trigger操作を利用できます。ページは毎フレームpollingせず、操作結果を自動再送しません。

状態機械では、状態を文字列化する関数と、診断へ明示的に公開するTriggerだけを登録します。

```csharp
using AnimationDiagnosticRegistration machineRegistration = registry.Register(
    "character.motion",
    machine,
    state => state.ToString(),
    new Dictionary<string, StateMachineTrigger>
    {
        ["jump"] = jumpTrigger,
    });
```

停止済み状態機械の再開始には`TContext`が必要なため、診断の`play`では開始しません。`play`はPause中の状態機械をResumeします。
`seek`は単体の`AnimationPlayback`だけを対象とします。対象や状態が操作に対応しない場合は`unsupported`を返します。

このパッケージは値の適用、全チャンネル値の転送、Compositionグラフの列挙、毎フレームの自動計測を行いません。
設計判断は[ADR-DIAGNOSTICS-0003](../../../docs/adr/diagnostics/DIAGNOSTICS-0003-animation-operations.md)を参照してください。
