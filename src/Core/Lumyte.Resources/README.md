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

## 世代とHotReload

ResourceReferenceは論理リソースを所有し、unmanagedなResourceHandleは非所有で参照します。
ResourceLeaseは内容世代、ResourceSnapshotは公開mapを保持します。
変更通知からSourceのRevisionを確認し、逆依存を再構築して検証した候補を一括公開します。
ReloadAsync／ReloadAllAsyncは内部のデバッグ用コマンドに限定し、公開APIには含めません。
失敗時は旧公開を保持し、競合はConflictを返します。

[ADR-RESOURCES-0002](../../../docs/adr/resources/RESOURCES-0002-generations-reload.md)を参照してください。

## ストリーミング

OpenStreamAsyncは不変factoryを保持し、独立したreaderとboundedな先読みbufferを作ります。
factoryの保持は旧入力ファイルの保存を意味しません。直接読み込み中の変更検出でsessionがfaultになった場合は開き直します。
`ReadAvailable`は待機・I/O・デコードを行わず、準備済みの要素だけを返します。blockの解放時には再利用するwork itemでproducerを通知し、readerの継続をconsumer上でinline実行しません。通知ごとのTaskやdelegateは生成しませんが、ThreadPool内部の初期化・queue拡張まで無割り当てとは保証しません。0はunderflowにもなるため`EndOfStream`と区別します。producerの満杯待機は通知方式、`ReadAsync`のunderflow待機は1 msの非同期pollです。デバイスcallbackでは`ReadAvailable`を使い、consumerは一つとしてreadとseekを並行させないでください。
producer・reader・buffer/scope・factory leaseの順で終了し、失敗した解放はTrimAsyncで再試行します。

[ADR-RESOURCES-0005](../../../docs/adr/resources/RESOURCES-0005-bounded-streaming.md)を参照してください。

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

`IResourceRuntime`はStageが必要とする実行先・device・owner・世代の借用をまとめるリソース環境です。
ResourceManagerは出力型の対応を固定し、選択したRuntimeを各内容世代に保存してBuildAsyncとReleaseAsyncへ使います。
Stageは`IResourceSourceStage<T, TRuntime>`または`IResourceTransform<TInput, TOutput, TRuntime>`で環境型を宣言し、`BuildAsync`の型付きRuntime引数で受け取れます。Stage側のキャストは不要です。選択した環境と宣言型の不一致はManager構築時に検出します。共通の`ResourceBuildContext.Runtime`も維持します。
既定のResourceRuntimeはPipeline開始とawait継続をジョブへ配送し、同じRuntime instanceを使う依存Stageは同じジョブ内で実行します。Runtime切り替えと独立Acquireでは別ジョブにし、キャッシュ済み・構築中の依存は共有します。モジュールRuntimeもこの共通基盤を利用できます。
ジョブ入口は`context.ExecutePipelineAsync(stage, token)`です。登録済み依存・Source操作の実完了までジョブを保持するため、親の取消で共有依存の継続先を失いません。依存・世代・解放は従来どおりStageごとに管理します。

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
