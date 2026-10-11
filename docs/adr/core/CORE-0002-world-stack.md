# ADR-CORE-0002: ワールドの寿命と独立したフレーム境界のスタック

- 状態: 採用
- 日付: 2026-10-11

## 背景

ゲームを保持したままポーズメニューや設定画面を重ねたい。スタックから外す操作とワールドの破棄を分離し、更新中の構造変更と不要な停止・再開通知を避ける必要がある。

## 決定

Core の Lumyte.Worlds に、ワールド管理、ワールドスタック、実行時シーンの基盤を追加する。アセット形式、物理空間、入力デバイス、描画バックエンドには依存しない。

WorldManager が World を生成・保持する。WorldStack は非所有参照と下位層の遮断設定を保持する。Pop は破棄しない。スタック外では更新・描画・入力を実行しない。同じワールドの重複配置と複数管理への所属は認めない。

ゲームループは入力前のフレーム境界で BeginFrame を呼ぶ。予約した Push / Pop / Replace と破棄を要求順に適用する。破棄対象の参照は最終スタックから除き、非実行状態を通知してから破棄する。操作の途中状態は通知せず、適用前後の状態差分だけを World が所属 Scene に通知する。通知中の要求は次回境界に回す。通知中・入力処理中・更新中・描画中の BeginFrame と、実行処理の再入を禁止する。

全ワールドの状態を確定してから通知する。通知例外は集約し、他のシーンへの通知と破棄を完了してから AggregateException として報告する。無効なスタック操作はバッチ全体を適用せず InvalidOperationException とする。

入力は上から下へ、描画と更新は有効な範囲を下から上へ行う。入力処理が true を返すと、そのフレームの下位入力を止める。時間は呼び出し側から渡し、停止中の時間を蓄積しない。全 API は単一のゲームループスレッドから呼ぶ。通知中のワールド生成とシーン構造変更は禁止する。

Scene は実行時インスタンスで、一つの World に所属する。追加・削除は非実行状態のワールドに限る。アセットのロードや非同期初期化は後続設計とする。

## 公開 API

比較元は main（2b40860）。対象 API は存在せず、以下は追加。

```diff
+namespace Lumyte.Worlds;
+// 実際に適用された更新・描画・入力の状態。default はすべて無効。
+public readonly record struct WorldExecutionState(bool UpdateEnabled, bool DrawEnabled, bool InputEnabled);
+// default は下位処理を遮断しない。
+public readonly record struct WorldLayerPolicy(bool BlocksUpdateBelow, bool BlocksDrawBelow, bool BlocksInputBelow);
+public abstract class Scene
+{
+    public World? World { get; }
+    public virtual void OnExecutionStateChanged(WorldExecutionState previous, WorldExecutionState current);
+    public virtual void Update(TimeSpan elapsed);
+    public virtual void Draw();
+    public virtual bool HandleInput();
+}
+public sealed class World
+{
+    public WorldExecutionState ExecutionState { get; }
+    public bool IsDestroyed { get; }
+    public IReadOnlyList<Scene> Scenes { get; }
+    // 破棄済み・実行中・所属済みシーンは拒否する。
+    public void AddScene(Scene scene);
+    public bool RemoveScene(Scene scene);
+}
+public sealed class WorldManager
+{
+    public WorldManager();
+    public WorldStack Stack { get; }
+    public IReadOnlyList<World> Worlds { get; }
+    public World CreateWorld();
+    // 境界で参照除去、通知、破棄。重複要求は無害。
+    public void RequestDestroy(World world);
+    public void BeginFrame();
+}
+public sealed class WorldStack
+{
+    // 配置順は下から上。変更は予約し、BeginFrame まで反映しない。
+    public IReadOnlyList<World> Worlds { get; }
+    public void Push(World world, WorldLayerPolicy policy = default);
+    // 空のスタックへの Pop は無害。Replace は空なら Push。
+    public void Pop();
+    public void Replace(World world, WorldLayerPolicy policy = default);
+    public void Update(TimeSpan elapsed);
+    public void Draw();
+    public void HandleInput();
+}
```

## 検討した代替案

- スタックがワールドを所有する: Pop 後の再利用と寿命の独立性を満たさない。
- 操作を即時適用する: 更新中の列挙と通知の再入が複雑になる。
- スタックがシーンに直接通知する: シーンの所属管理がスタックに漏れる。
- 単一のポーズフラグ: 入力だけ遮断する場合や背景だけ描画する場合を表現できない。

## 結果と影響

ポーズと設定画面の重ね合わせ、ワールドの保持・再配置が可能になる。フレーム境界の呼び出しと実時間の提供はゲームループの責務になる。Scene の動的追加、非同期ロード、シリアライズ、音声、オンラインでの停止方針は別途決定する。

## 検証方針

予約の遅延、三種の遮断と処理順、再配置、途中状態の通知抑制、通知中の予約、参照除去後の破棄、無効バッチと通知例外を単体テストする。
