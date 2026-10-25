# Lumyte.Resources

出力型を識別子とするPipelineで、共有読み込みと依存を管理します。
`AcquireAsync<T>(ResourceKey<T>)`は論理リソースを保持するReferenceを返します。
描画開始など値を固定したい時点でReference.Acquire()から値とRevision付きのLeaseを取得します。
Stageは`RequireAsync`で依存を追加し、`OpenSourceAsync`で観測した入力のdescriptorを借用します。
読み込みの整合性と変更検出の限界はSourceが定め、旧ファイルの保存は要求しません。
Build依存は構築後、Resident依存は親の解放後に返します。
Memory scopeはmanagerが所有し、出力・cleanup・snapshot・memory・依存の順に解放します。
解放失敗は所有を保持して`TrimAsync`で再試行します。
同期的な補助所有物は`context.RegisterCleanup(disposable, lifetime)`で登録でき、捕捉ラムダを必要としません。
Build寿命の入力を完了通知や成功済みcleanupが保持し続けないよう、不要になった参照を切ります。
Source・Stage・Runtimeは借用します。利用権を返し、managerを閉じてからRuntimeを終了します。

出力と主入力は非nullの参照型・値型を扱えます。参照一本のreadonly structもその型のまま登録・取得できます。
値型は構築時に一度boxして世代へ保持し、Lease.Valueや依存取得はunboxしたコピーを返すため、読み取りごとのboxはありません。
コピーは参照先の所有権や利用期間を延ばしません。借用値を扱う間はLeaseまたはResident依存を保持します。
解放の進行状態はコピーされるstructのfieldではなく元の所有オブジェクトで保持してください。
参照型の同一instanceを別出力として返す検出は維持しますが、値型の内部参照の別名関係は検査しません。

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

`IResourceRuntime`はStageが必要とする安定した環境で、実行器のExecutorと世代providerのDependenciesを提示します。
ResourceManagerは出力型ごとの環境を選び、同じ環境の実行器と依存一覧を構築時に一度取得します。
CPUだけの環境は空のDependenciesを使い、複合環境は複数providerを提示できます。
既定のResourceRuntimeは環境と共通実行器を兼ね、モジュールRuntimeも同じExecutorを利用できます。

`IResourceSourceStage<T, TRuntime>`と`IResourceTransform<TInput, TOutput, TRuntime>`はBuildとReleaseで型付きRuntimeを受け取ります。
Stage側のキャストは不要で、環境と宣言型の不一致はManager構築時に検出します。
RuntimeはStage用サービスを提供し、Source・メモリ・依存・cleanupの使用記録はResourceBuildContextを通してManagerが所有します。
共有Runtimeに構築ごとの可変状態を保持しません。

Executorはジョブ内で`context.ExecuteAsync(token)`を呼び、ManagerがStage呼出しから出力の所有記録、依存の静止待ち、cleanupまでを実行します。
同じRuntime instanceを使う依存Stageは同じジョブで同じManager手順を通ります。
Runtime切り替えと独立Acquireは別ジョブにし、キャッシュ済み・構築中の依存は共有します。
親の待機が取消されても共有依存の実完了を待ち、継続先を保持します。

I/O・依存ロード・所有スレッド待機の間はCPU枠を返します。
StageとCPU処理を含むhelper、待機後に次のownerコマンドを送る環境メソッドは通常のawaitを使います。
ConfigureAwait(false)やTask.Runで移した処理はスケジューラーの保証対象外です。
Host/CooperativeではPumpで進め、非同期待機中もホストのループを止めません。
Stage全体への固定スレッド親和性は保証せず、native操作はRuntimeが提供するownerコマンドへ配送します。

構築ごとに取得したdevice Leaseの実際のidentity・Versionを記録し、公開前に検証します。
旧出力の解放には生成時のRuntimeと元の世代Lease・ownerを使い、現在のdeviceを取り直しません。
初回ロードと再構築は同じStageとManagerの手順を使います。
Reference・Lease・Snapshotを返し、Managerの構築と解放を完了してから、ホストが共有実行器とdevice環境を停止します。

[Runtime設計](../../../docs/adr/resources/RESOURCES-0003-runtime-execution.md)を参照してください。
