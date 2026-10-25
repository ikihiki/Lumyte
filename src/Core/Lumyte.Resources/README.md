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

設計判断は[ADR-RESOURCES-0001](../../../docs/adr/resources/RESOURCES-0001-typed-pipelines.md)を参照してください。

## 世代とHotReload

ResourceReferenceは論理リソースを所有し、unmanagedなResourceHandleは非所有で参照します。
ResourceLeaseは内容世代、ResourceSnapshotは公開mapを保持します。
変更通知からSourceのRevisionを確認し、逆依存を再構築して検証した候補を一括公開します。
ReloadAsync／ReloadAllAsyncは内部のデバッグ用コマンドに限定し、公開APIには含めません。
失敗時は旧公開を保持し、競合はConflictを返します。

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

## リロードの範囲

入力ファイルのRevisionが変わった場合、その入力を直接使うリソースと、依存する親だけを再構築します。
影響のない別の枝と公開済み依存先は再利用します。無変更通知・重複通知・未追跡入力では追加の構築を行いません。
回収済みの依存先が構築に必要なら再取得します。同一ファイル内の変更部分の判定や、
出力の同値判定による親への伝播停止は行いません。

## Referenceとフレーム用Lease

Managerの`AcquireAsync`とhandleからの`TryAcquire`はReferenceを返します。
Referenceを利用期間中保持し、描画・ジョブの開始時に`reference.Acquire()`で具体的な値とRevisionを固定します。
複数リソースを一つの公開状態から取得する場合は`CaptureSnapshot()`で取得した`ResourceSnapshot`を`reference.Acquire(snapshot)`へ渡します。
Lease取得はI/Oや構築を行いません。Reference返却後も、取得済みLeaseの値は利用できます。
HotReloadは新世代を別に公開するため、取得済みLeaseは取得時の値とRevisionを保持します。
公開済みの値そのものを直接変更しないでください。Leaseは書き換え処理を待たせるmutexではありません。

## 自動変更監視

```csharp
// sourceがStreamと、その取得元に対応するChangesを提供する。
// resourceRuntimeは前述の手順で起動済みのExecutionRuntimeへ登録する。
await using var manager = new ResourceManager(
    "assets", source, stages, memory, resourceRuntime);
using ResourceReference<MyResource> reference = await manager.AcquireAsync(
    new ResourceKey<MyResource>("asset.bin"));
// ファイル編集などの通知で、Managerが必要な依存と親だけを再構築する。
using ResourceLease<MyResource> frame = reference.Acquire();
```

`IResourceSource.Changes`が通知元を返すと、Managerがその監視を自動開始します。Streamを開く取得元と監視先の対応はSourceが保証します。Changesの既定値はnullで、不変なDLL埋め込みなどでは監視を開始しません。Managerは`IResourceWatchTargets`としてロード中・公開中の入力ID集合を監視側へ渡します。各集合は不変で、次回取得時に最新となります。`ResourceReloadService`は通知をまとめ、IDの重複を除いてHotReloadします。連続更新で待機し続けないよう集約時間に上限を設け、再構築中に届いた通知も次のバッチへ保持します。保留できるID数を超えた場合や`ResourceSourceChange.Rescan`を受信した場合は、追跡中の入力をすべて再確認します。

`Conflict`と構築・入力読み取りの失敗は、設定された間隔・上限回数で再試行します。失敗時は旧公開値を維持し、再試行を使い切った後も新しい通知は受け付けます。`LastResult`・`LastError`と`Reloaded`・`ReloadFailed`で経過を確認でき、監視列挙自体の失敗は`Completion`へ伝わります。イベントはバックグラウンド処理上で実行されるため、UIなどへの配送は利用側で行います。

DIでは`UseSource<TSource>()`だけで監視も接続します。Managerは終了時に監視と進行中の自動再構築をキャンセルしてjoinしてからリソースを解放します。HotReload専用の`Start`／`StopAsync`は不要ですが、ExecutionRuntimeは先に起動し、Managerの解放後に停止します。`manager.HotReload`から結果・失敗・`Completion`を観測できます。通知元はSourceに属し、Managerは購読の寿命を管理します。`ConfigureHotReload(new() { Enabled = false })`で購読を無効にでき、DIを使わない場合はconstructorの`reloadOptions`へ設定します。複合SourceではStreamの振り分けと同じ規則で監視対象を振り分けてください。

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
