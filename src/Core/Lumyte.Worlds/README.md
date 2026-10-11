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
manager.Stack.Update(realFrameDuration);
manager.Stack.Draw();

// 再開。pause のデータは保持する。
manager.Stack.Pop();
manager.BeginFrame();

// 不要になったワールドは明示的に破棄する。
manager.RequestDestroy(pause);
manager.BeginFrame();
```

`Scene` を継承し、`Update`、`Draw`、`HandleInput`、
`OnExecutionStateChanged` の必要な処理だけを実装する。
UI は実時間で更新し、ゲーム側は有効なフレームだけ更新する。
入力を消費したシーンは `HandleInput` で `true` を返す。
デバイス入力の収集、カメラ、音声、物理、アセットの読み込みは利用側が接続する。

全操作は単一スレッドで行う。スタック操作は予約され、通知中の予約は次回境界に回る。
同じワールドを二度積む要求は境界でバッチ全体を拒否し、そのスタック操作バッチは破棄する。
破棄要求は成功する境界まで保持する。通知例外は状態確定と破棄完了後に集約して返す。
シーンの追加・削除は非実行状態かつ境界処理外で行う。
ワールドは一つの管理に所属し、破棄後は再利用できない。

```sh
dotnet test tests/Core/Lumyte.Worlds.Tests -c Release
```
