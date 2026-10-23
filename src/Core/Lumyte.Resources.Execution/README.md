# Lumyte.Resources.Execution

`ResourceRuntime`は`IResourceRuntime`と`IExecutionModule`を実装する既定のリソース環境です。
Stageの構築・解放をExecutionのworkflowジョブへ配送し、複数Managerで共有します。
Audio・GraphicsなどのRuntimeもIResourceRuntimeを実装し、この共通ジョブ環境とdevice・owner・世代の管理を組み合わせます。
初回ロードと再構築は同じ経路を使い、Stageへのジョブ注入は不要です。

## DIを使わない起動

一つのホストでは同じResourceRuntimeを複数Managerで共有します。
portはRuntime instanceに属し、独立したホスト間では共有しません。

```csharp
var resourceRuntime = new ResourceRuntime();
var execution = new ExecutionRuntime([resourceRuntime]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

await using (var manager = new ResourceManager(
    "assets", source, [new DocumentStage(), new MeshStage()], memory, resourceRuntime))
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await execution.StopAsync();
```

DIの`AddResources`は既定RuntimeとExecutionRuntimeを自動登録します。
[DIの登録・起動例](../Lumyte.Resources.DependencyInjection/README.md)を参照してください。
DI解決だけでは起動せず、Prepare/Start/Pump/Stopはホストが実行します。

## 非同期Stageの契約

Stageの開始と通常の`await`の継続を、ThreadPoolではworker、Cooperativeではホストの`PumpJobs`へ配送します。
I/O・依存ロード・所有スレッドの処理を待つ間はCPU枠を解放し、並列数1でも子のジョブを進められます。
ThreadPoolでは同じworkerに戻る保証はありません。
開始したStageは実際の完了まで追跡し、取消要求だけで入力や未回収の出力を捨てません。

StageとCPU処理を含む補助メソッドは通常の`await`を使ってください。
`ConfigureAwait(false)`、Task.Run、独自スレッドによる処理はCPU並列度とPump予算の保証対象外です。
Sourceやdevice service内部の`ConfigureAwait(false)`は使用できます。呼出側のStageが通常のawaitで実行先を復元します。
同期区間は途中で中断できないため、大きな変換は有限の区間に分け、Task.Yieldのawaitなどで次の区間をキューへ戻します。
workflow内からジョブやownerコマンドを送れますが、同期Wait/Resultや再入Pumpは行いません。

## StageごとのRuntime

Managerの`stageRuntimes`へ出力型とRuntimeの対応を渡すと、そのStageの環境を選択できます。
未指定のStageは既定Runtimeを使い、選択した環境は`ResourceBuildContext.Runtime`で取得できます。
次のMeshRuntimeは、共通ジョブ環境とdevice serviceを組み合わせるアプリ定義の安定した窓口です。

```csharp
var meshRuntime = new MeshRuntime(resourceRuntime, deviceService);
await using var manager = new ResourceManager(
    "assets", source, stages, memory, resourceRuntime,
    stageRuntimes: [new(typeof(Mesh), meshRuntime)]);
```

device用のmoduleもExecutionRuntimeの起動前に登録してください。
モジュールRuntimeはCPU処理とowner処理を調整し、Stage全体を固定スレッドへ置くものではありません。
Stageは選択Runtimeから構築ごとに現在のdevice世代を借り、実際のVersionを依存として記録します。
Runtimeが`IResourceRuntimeDependency`を実装していればManagerが同じinstanceをproviderとして自動収集します。
解放は内容世代に保存したRuntimeと生成時のLease・ownerを使い、現在のdeviceを取り直しません。

## 容量と終了

配送は型付きstructを使い、送信ごとのラムダやTask.Runを追加しません。
非同期状態機械や継続の確保は残り、無割り当てを保証するものではありません。
起動前・停止後の配送は拒否し、容量不足は`ExecutionQueueFullException`で通知します。
受付容量は返したValueTaskの結果を一度awaitして回収するまで保持します。停止処理はworkflowの実完了を待ち、結果の回収は待ちません。
未開始の処理は取消で省略できますが、開始後はCancelPendingでも継続を維持し、協調取消と実完了を待ちます。

BrowserではHost/Cooperativeで配置し、ロード・解放の待機中もイベントループからPumpを続けます。
Reference・Lease・Snapshotを返し、Managerの構築と解放を完了してからExecutionRuntimeを停止してください。
解放失敗時は所有を保持して再試行するため、解放に必要なRuntimeとownerを先に止めません。

[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)を参照してください。
