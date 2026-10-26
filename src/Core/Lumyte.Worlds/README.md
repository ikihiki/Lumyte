# Lumyte.Worlds

ワールドの寿命と、入力・更新・描画を制御するスタックを独立して管理する。
[設計と契約](../../../docs/adr/core/CORE-0002-world-stack.md)を参照。

```csharp
var manager = new WorldManager();
World game = manager.CreateWorld();
World pause = manager.CreateWorld();
game.AddScene(gameScene);
pause.AddScene(pauseMenuScene);
manager.Stack.Push(game);

// ポーズを開く要求。ゲームの描画は継続する。
manager.Stack.Push(pause, new WorldLayerPolicy(true, false, true));

// 毎フレーム、入力を収集した後、処理する前の境界で適用する。
manager.BeginFrame();
manager.Stack.HandleInput();
// 固定更新のスケジューリングはゲームループが担当する。
// pendingFixedSteps は停止時間を含めず、追いつき回数に上限を設けて計算する。
for (int i = 0; i < pendingFixedSteps; i++)
{
    manager.Stack.FixedUpdate(fixedStep);
}

// 全シーンのUpdate完了後、全シーンのLateUpdateを内部で実行する。
manager.Stack.Update(realFrameDuration);
manager.Stack.Draw(drawContext);
// この後、描画システムが登録結果を実行し、Presentする。

// 再開。pause のデータは保持する。
manager.Stack.Pop();
manager.BeginFrame();

// 不要になったワールドは明示的に破棄する。
manager.RequestDestroy(pause);
manager.BeginFrame();
```

`Scene` を継承し、`FixedUpdate`、`Update`、`LateUpdate`、`Draw`、`HandleInput`、
`OnExecutionStateChanged` の必要な処理だけを実装する。
UI は実時間で更新し、ゲーム側は有効なフレームだけ更新する。
入力を消費したシーンは `HandleInput` で `true` を返す。
デバイス入力の収集、カメラ、音声、物理、アセットの読み込みは利用側が接続する。

`FixedUpdate` のstepは正、`Update` のelapsedは非負とする。
`BlocksUpdateBelow` は固定更新・通常更新・LateUpdateのすべてを停止する。
各固定ステップではBeginFrameを呼ばず、同じ確定済みスタックを使用する。
固定更新の時間蓄積、最大追いつき回数、固定更新用の入力状態はゲームループが管理する。
入力の「押した瞬間」を複数の固定ステップで繰り返し消費しないようにする。
通常更新やLateUpdateで同じ物理状態を重ねて進めない。

`DrawContext` は利用側が継承して、描画登録先を追加する。
例えばGraphics統合側では次のようなコンテキストを用意できる。
CoreのWorldsモジュールはGraphicsへ依存しない。

```csharp
using Lumyte.Graphics.RenderGraph;
using Lumyte.Worlds;

public sealed class GameDrawContext(
    RenderGraph graph,
    TimeSpan elapsed,
    double alpha = 1) : DrawContext(elapsed, alpha)
{
    public RenderGraph Graph { get; } = graph;
}
```

シーンの `Draw(DrawContext context)` から、この型を通してRenderGraphへパスを登録する。
同じ描画呼び出しでは全シーンへ同一コンテキストを渡す。
コンテキストと参照先の寿命は呼び出し側が管理し、シーンは保持・破棄しない。
`Elapsed` は非負の実フレーム時間、`InterpolationAlpha` は0から1の有限値である。
補間しない場合の係数は1。補間する場合はゲームループが残り時間から計算する。
停止中のワールドでは最後に表示した状態を固定し、変化する係数で動かさない。
GPU送信・実行・Presentはシーンの責務に含めない。

全操作は単一スレッドで行う。スタック操作は予約され、通知中の予約は次回境界に回る。
同じワールドを二度積む要求は境界でバッチ全体を拒否し、そのスタック操作バッチは破棄する。
破棄要求は成功する境界まで保持する。通知例外は状態確定と破棄完了後に集約して返す。
シーンの追加・削除は非実行状態かつ境界処理外で行う。
ワールドは一つの管理に所属し、破棄後は再利用できない。

```sh
dotnet test tests/Core/Lumyte.Worlds.Tests -c Release
```
