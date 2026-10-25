# Lumyte.Resources.Execution

Resourcesの既定環境と、Executionのworkflowジョブを使う実行器を提供します。
`ResourceRuntime`は`IResourceRuntime`、`IResourceExecutor`、`IExecutionModule`を実装し、すべてのStageで共有する既定環境です。

## 登録と起動

DIなしでは、同じResourceRuntimeをExecutionRuntimeとResourceManagerへ渡します。
例はホストイベントループへ必要なPumpを接続済みであることを前提とします。

```csharp
var resourceRuntime = new ResourceRuntime();
var execution = new ExecutionRuntime([resourceRuntime]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

await using (var manager = new ResourceManager(
    "assets", source, stages, memory, resourceRuntime))
{
    using ResourceReference<Document> document = await manager.AcquireAsync(documentKey);
    using ResourceLease<Document> lease = document.Acquire();
    Consume(lease.Value);
}

await execution.StopAsync();
```

DIの`AddResources`は既定RuntimeとExecutionRuntimeを自動登録します。
[DIの登録・起動例](../Lumyte.Resources.DependencyInjection/README.md)を参照してください。
DI解決だけでは起動せず、Prepare/Start/Pump/Stopはホストが実行します。

## 環境と実行器

`IResourceRuntime`はStageの安定した環境で、ExecutorとDependenciesを提示します。
Audio・Graphicsの環境は既定RuntimeのExecutorを共有し、固有サービスとowner操作を型付きで提供できます。
出力型ごとの環境はManagerのstageRuntimes、またはDIのUseStageRuntimeで選びます。未指定のStageは既定環境を使います。

Managerは同じ環境instanceのExecutorとDependenciesを構築時に一度取得し、登録済みの実行器とproviderを使用します。
複合環境は複数の世代providerを提示でき、CPUだけの環境は空のDependenciesを使えます。
同じprovider instanceは重複除去し、同名の別instanceは拒否します。登録後の構成変更には依存しません。

`IResourceSourceStage<T, TRuntime>`と`IResourceTransform<TInput, TOutput, TRuntime>`は、BuildとReleaseで選択されたRuntimeを型付きで受け取ります。
Manager構築時に環境型を検証するため、Stage側のキャストは不要です。
構築ごとに実際のdevice世代を借り、Contextへidentity・Version・Leaseの寿命を記録してください。
解放には生成時のRuntimeと元のdevice Lease・ownerを使い、現在のdeviceを取り直しません。

独自Executorはジョブ内で`context.ExecuteAsync(token)`を呼びます。
これはManagerの構築手順を実行する入口であり、Stage呼出し、出力の所有記録、依存の静止待ち、cleanupをManagerが管理します。
親の待機が取消されても、登録済み依存・Sourceが実完了するまでジョブを保持します。
Stage自身が他のStageを実行する場合はRequireAsyncを使い、この実行器用入口を呼びません。

## 非同期Stageの契約

構築開始と通常のawait後の継続を、ThreadPoolではworker、CooperativeではホストのPumpJobsで実行します。
同じRuntime instanceを使う依存Stageは、新しいworkflowを作らず同じジョブ内でManagerの共通手順を通ります。
Runtime切り替え・独立Acquireは別ジョブです。`A → B → A`は3区間で、共通Executorを持つだけの別環境も統合しません。
キャッシュ済み・構築中の依存は結果を共有し、依存・世代・解放はStageごとに管理します。

I/O・依存ロード・所有スレッドを待つ間はCPU枠を返し、並列数1でも子の仕事を進められます。
ThreadPoolでは同じworkerに戻る保証はありません。
StageとCPU処理を含むhelper、待機後に次のownerコマンドを送る環境メソッドは通常のawaitを使ってください。
ConfigureAwait(false)、Task.Run、独自スレッドへ移した処理はCPU並列度とPump予算の保証対象外です。
Sourceやdevice service内部の終端処理で文脈を外した場合も、呼出側のStageは通常のawaitでジョブへ戻します。
同期Wait/Resultや再入Pumpは行いません。

既定ExecutorはExecutionRuntime.ShouldYieldを用い、現在の同期実行区間が約1msを超えたかを判定します。
I/Oやキューで待った時間を含めず、再開ごとに計測を始め直します。Managerは依存境界でこの判断を使います。
深い同期呼び出しには別途32段でyieldし、stackを解放します。
同期Stage自体を途中中断する機能はないため、大きなCPU変換はStage側で有限の区間に分けます。

## ストリーミング

`OpenStreamAsync`はfactoryの出力型に登録したExecutorへ、reader生成・先読み・Seek・解放を配送します。
`ResourceRuntime.StreamAsync`は型付きworkflowを使い、データやbuffer空きの待機中はCPU枠を返します。
producerは停止・終端まで一つの受付枠を保持するため、`MaxInFlightRuns`には同時sessionと通常の構築・解放を含めてください。
独自Executorもstreamingを使う場合は`StreamAsync`を実装し、ジョブ内で渡された`IResourceStreamWork.ExecuteAsync`を一度実行します。
Cooperativeではsessionの生成・Seek・解放を待つ間もPumpを続けます。
詳細は[ストリーミングの契約](../../../docs/adr/resources/RESOURCES-0005-bounded-streaming.md)を参照してください。

## 容量と終了

配送は型付きstructと既存Contextを使い、送信ごとのラムダや汎用sessionを追加しません。
非同期状態機械や継続の確保は残り、無割り当ては保証しません。
起動前・停止後の配送は拒否し、容量不足はExecutionQueueFullExceptionで通知します。
受付容量はRuntime区間ごとに予約し、実行器がworkflowの結果を一度観測して回収するまで保持します。
停止は実完了を待ち、未観測結果の回収は待ちません。
未開始の処理は取消で省略できますが、開始後はCancelPendingでも継続を維持し、協調取消と実完了を待ちます。

BrowserではHost/Cooperativeで配置し、ロード・解放の待機中もイベントループからPumpを続けます。
sessionを解放し、Reference・Lease・Snapshotを返して、Managerの構築と解放を完了してからExecutionRuntimeを停止してください。
解放失敗時は所有を保持して再試行するため、Manager・DI provider・実行器・device環境を維持します。
Managerは環境の利用を管理し、共有deviceやworkerの物理的な起動・停止はホストが管理します。

[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)を参照してください。
