# Lumyte.Resources

出力型を識別子とするPipelineで、共有読み込みと依存を管理します。
`AcquireAsync<T>(ResourceKey<T>)`は論理リソースを保持するReferenceを返します。
描画開始など値を固定したい時点でReference.Acquire()から値とRevision付きのLeaseを取得します。
Stageは`RequireAsync`で依存を追加し、`OpenSourceAsync`で観測した入力のdescriptorを借用します。
読み込みの整合性と変更検出の限界はSourceが定め、旧ファイルの保存は要求しません。
Build依存は構築後、Resident依存は親の解放後に返します。
Memory scopeはmanagerが所有し、出力・cleanup・snapshot・memory・依存の順に解放します。
解放失敗は所有を保持して`TrimAsync`で再試行します。
Source・Stage・Runtimeは借用します。利用権を返し、managerを閉じてからRuntimeを終了します。

設計判断は[ADR-RESOURCES-0001](../../../docs/adr/resources/RESOURCES-0001-typed-pipelines.md)を参照してください。

## ハンドルと内容世代

ResourceReferenceは論理リソースを所有し、unmanagedなResourceHandleは非所有で参照します。
ResourceLeaseは内容世代、ResourceSnapshotは公開mapを保持します。
[ADR-RESOURCES-0002](../../../docs/adr/resources/RESOURCES-0002-generations-reload.md)を参照してください。

## DIを使わない構築

`IResourceSourceStage<T>`と`IResourceTransform<TInput, TOutput>`は共通の`IResourceStage`を実装します。
Stage instanceを直接渡せるため、Pipelineラッパーは不要です。一つのStage instanceは一つの出力型を担当します。
Source・Stage・Memory・IResourceRuntimeは借用であり、managerを閉じるまで呼出側が保持します。
既定のResourceRuntimeをExecutionRuntimeのmodule集合へ登録し、起動してからロードします。
出力型ごとの環境はstageRuntimesで選択し、指定のないStageは既定Runtimeを使います。

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

特定の出力だけにdevice環境が必要なら、Manager構築時に対応を追加します。
次のMeshRuntimeは、既定ジョブ環境とdevice serviceを組み合わせるアプリ定義の例です。
deviceのmoduleもExecutionRuntimeへ起動前に登録してください。

```csharp
var meshRuntime = new MeshRuntime(resourceRuntime, deviceService);
await using var manager = new ResourceManager(
    "assets", source, stages, memory, resourceRuntime,
    stageRuntimes: [new(typeof(Mesh), meshRuntime)]);
```

型付きインターフェースの既定実装が主入力の取得と型付き呼び出しを行います。
DI登録も同じStageを直接渡し、必要なサービスをコンストラクターへ注入します。
[既定Runtime](../Lumyte.Resources.Execution/README.md)と[DI登録](../Lumyte.Resources.DependencyInjection/README.md)も参照してください。

## Referenceとフレーム用Lease

Managerの`AcquireAsync`とhandleからの`TryAcquire`はReferenceを返します。
Referenceを利用期間中保持し、描画・ジョブの開始時に`reference.Acquire()`で具体的な値とRevisionを固定します。
複数リソースを一つの公開状態から取得する場合は`CaptureSnapshot()`で取得した`ResourceSnapshot`を`reference.Acquire(snapshot)`へ渡します。
Lease取得はI/Oや構築を行いません。Reference返却後も、取得済みLeaseの値は利用できます。
取得済みLeaseは、取得時の値とRevisionを保持します。
公開済みの値そのものを直接変更しないでください。Leaseは書き換え処理を待たせるmutexではありません。

## Runtimeと非同期ロード

`IResourceRuntime`はStageが必要とする実行先・device・owner・世代の借用をまとめるリソース環境です。
ResourceManagerは出力型の対応を固定し、選択したRuntimeを各内容世代に保存してBuildAsyncとReleaseAsyncへ使います。
Stageは`IResourceSourceStage<T, TRuntime>`または`IResourceTransform<TInput, TOutput, TRuntime>`で環境型を宣言し、`BuildAsync`の型付きRuntime引数で受け取れます。Stage側のキャストは不要です。選択した環境と宣言型の不一致はManager構築時に検出します。共通の`ResourceBuildContext.Runtime`も維持します。
既定のResourceRuntimeは開始とawait継続をジョブへ配送し、モジュールRuntimeもこの共通ジョブ環境を利用できます。

I/O・依存ロード・所有スレッド待機の間はCPU枠を解放します。
StageとCPU処理を含む補助メソッドは通常のawaitを使い、同期区間を有限に保ちます。
ConfigureAwait(false)やTask.Runで逃がした処理はスケジューラーの保証対象外です。
Host/CooperativeではPumpで処理を進め、非同期待機中もホストのループを止めません。
RuntimeはCPU処理とowner処理を調整し、Stage全体への固定スレッド親和性は保証しません。

RuntimeがIResourceRuntimeDependencyを実装すると、Managerはproviderを自動収集します。
Stageは構築ごとに現在のdevice世代を借り、実際のidentity・versionを記録して公開前に検証します。
生deviceをStageに固定せず、旧出力の解放には生成時の世代Lease・ownerを使います。
初回ロードと変更後の再構築は同じStageと選択Runtimeを使います。
監視と新規ロードを止め、リソース解放を完了してからExecutionRuntimeと各環境を終了してください。

[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)を参照してください。
