# Lumyte.Resources

出力型を識別子とするPipelineで、共有読み込みと依存を管理します。Graphics・Audio・Platformの実装には依存しません。
`AcquireAsync<T>(ResourceKey<T>)`は論理リソースを保持するReferenceを返します。
描画開始など値を固定したい時点でReference.Acquire()から値とRevision付きのLeaseを取得します。
Stageは`RequireAsync`で依存を追加し、`OpenSourceAsync`で観測した入力のdescriptorを借用します。
読み込みの整合性と変更検出の限界はSourceが定め、旧ファイルの保存は要求しません。
Build依存は構築後、Resident依存は親の解放後に返します。
Memory scopeはmanagerが所有し、出力・cleanup・snapshot・memory・依存の順に解放します。
解放失敗は所有を保持して`TrimAsync`で再試行します。
Source・Stage・Runtimeは借用します。利用権を返し、managerを閉じてからRuntimeを終了します。

## Pipelineと依存

`ResourceKey<T>`は`ResourceAddress`と`T`本体の組み合わせです。Pipelineの登録・検索、内部キー、依存解決には`typeof(T).TypeHandle`を使い、型名の文字列を識別子にしません。manager内では出力型ごとに一つのPipelineを登録し、transformの入力も`TInput`の型から解決します。`Document → CpuMesh → GpuMesh`のようにCPU・GPUや意味が異なる出力はそれぞれの型で表現します。

AddressのSubresourceで単一ファイル内の要素、Variantで同じ型の変換条件を選びます。閉じたジェネリック型や、別のAssemblyLoadContextに読み込まれた同名型も、それぞれ別の型として扱います。Pipelineの型handleはプロセス内の識別用で、ファイルへの保存や通信には使いません。複合リソースは具体型ごとに子への参照またはResourceHandleを保持します。具体的な子の借用値を保持する場合はResident依存を記録し、子の寿命を親まで延長します。

```csharp
pipelines
    .AddSourcePipeline<SceneDocument, DocumentStage>()
    .AddTransformPipeline<SceneDocument, CpuMesh, MeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, UploadStage>();

var key = new ResourceKey<CpuMesh>("scene.json", "mesh/0");
using ResourceReference<CpuMesh> reference = await resources.AcquireAsync(key);
using ResourceLease<CpuMesh> mesh = reference.Acquire();
```

通常はパス文字列から直接キーを作成します。SubresourceとVariantは省略でき、`new ResourceKey<SceneDocument>("scene.json")`や`new ResourceKey<CpuMesh>("scene.json", "mesh/0", variant: "geometry:v1")`と記述できます。既存の`ResourceId`または`ResourceAddress`を渡すこともできます。

ステージは`ResourceBuildContext.RequireAsync`で依存を要求します。互換な構築view内の同じキーを一つの構築へ集約し、別々に開始した構築間の循環も検出します。入力そのものを別の所有出力として返すことはできません。値は共有されるため、利用中は読み取り専用として扱います。

`Build`依存は構築とその片付けまで、`Resident`依存は出力の解放完了まで保持します。同じ依存を両方の寿命で要求した場合はResidentへ引き上げます。contextの操作はステージ内で完了までawaitし、ステージからmanagerを直接呼んで依存を隠さないでください。

ファイルを読む操作も`OpenSourceAsync`を経由します。snapshotは観測した入力Revisionを表すdescriptorで、contextが所有します。入力の整合性と変更検出の限界はSourceが定めます。FileSystemは直接読み込みを行い、変更を検出すると失敗します。旧ファイルの保存は行いません。ステージが開いたstreamはステージが閉じます。具体的な取得元には`Lumyte.Resources.FileSystem`を使用できます。

DLLの埋め込みデータには`Lumyte.Resources.Embedded`の`EmbeddedResourceSource`を使用できます。Assemblyと必要に応じて論理ID・manifest resource名の対応をDIで渡し、同じPipelineから読み込みます。埋め込みデータの範囲読み込みも可能です。

## 所有権とメモリ

- `ResourceLease<T>`は内容Revisionを固定し、返却はI/OやGPU待機を行いません。
- `ResourceReference<T>`は論理キーを常駐させ、HotReload後は新しい公開世代を保持します。
- referenceの`ResourceHandle<T>`は非所有のunmanagedな16 byteの値で、ECSへ格納できます。最後のreference返却で失効し、slot再利用時はGenerationが変わります。
- `CaptureSnapshot`は一つの公開mapを保持します。フレーム内の複数handleをそのsnapshotで解決すると、新旧の組み合わせが変わりません。snapshot返却後も発行済みleaseは有効です。

利用者は値を直接Disposeせず、利用終了までleaseを保持します。handle自体のコピーは所有数を増やしません。unmanagedジョブへ渡すデータはmanaged側でhandleを解決して用意し、ジョブやGPU使用の完了までleaseを保持してください。

ステージはcontextからBuild用・Resident用の`IMemoryScope`を借ります。CPUメモリは`RentAsync`、Native資源は予約・物理確保・commitの順で計上します。出力の`ReleaseAsync`、補助cleanup、snapshot、memory scope、依存利用権の順で解放します。失敗した処理と後続の所有物は保持し、`TrimAsync`で再試行します。成功したcleanupは再実行しません。予約失敗からの自動回収は行いません。

`TrimAsync`は未使用の親から解放し、その結果未使用になった依存も同じ呼び出しで回収します。managerはMemoryのreclaimerへ登録されます。返却byte数は共有poolの累積解放カウンターの差分で、チャンク全体の解放と並行した返却を含みます。個々のsliceサイズを合計する方式ではありません。

## HotReloadとruntime世代

Managerは変更通知を入力Revisionの再確認のきっかけとして使い、逆依存をたどって候補を構築します。Build依存の値をTrimした後も、親が残る間は再構築用の依存情報を保持します。候補の構築・入力確認がすべて成功した後、値とhandleの解決先を一括公開します。途中で入力や公開mapが変わった場合は`Conflict`になり、候補を片付けます。構築失敗では例外を返し、旧公開値を維持します。

通常の新規取得でもsourceの変更を検出した場合、旧親と新しい中間値だけが混在する公開を拒否します。通知元から変更したIDを通知し、HotReloadの完了後に取得を再試行してください。既存の公開値への取得は、Reloadが公開するまでその世代を使用します。通知されたIDのうち追跡中の入力だけを再確認し、未ロードの無関係なファイルは読み込みません。Rescan通知では追跡中のすべての入力Revisionを再確認します。`ReloadAsync`／`ReloadAllAsync`は内部のデバッグ用コマンドで、公開APIには含めません。

既存lease・resource snapshotは構築済みの旧出力を保持し、stream sessionは旧factoryを保持します。外部入力の旧ファイルは保存しないため、直接読み込み中の変更では既存sessionも失敗し得ます。runtime依存の不可逆な失効は例外で、Resident依存する値とその親を利用不能にします。論理referenceとhandleは維持し、復旧後の`RebuildRuntimeAsync`で新世代へ接続します。Buildだけでruntimeを使った独立CPU値は利用を継続できます。Graphicsとの接続は`Lumyte.Resources.Graphics`を使用します。

## 自動変更監視

```csharp
// sourceがStreamと、その取得元に対応するChangesを提供する。
// resourceRuntimeは「DIを使わない構築」の手順で起動済みのExecutionRuntimeへ登録する。
await using var manager = new ResourceManager(
    "assets", source, stages, memory, resourceRuntime);
using ResourceReference<MyResource> reference = await manager.AcquireAsync(
    new ResourceKey<MyResource>("asset.bin"));
// ファイル編集などの通知で、Managerが必要な依存と親だけを再構築する。
using ResourceLease<MyResource> frame = reference.Acquire();
```

`IResourceSource.Changes`が通知元を返すと、Managerがその監視を自動開始します。Streamを開く取得元と監視先の対応はSourceが保証します。Changesの既定値はnullで、不変なDLL埋め込みなどでは監視を開始しません。Managerは`IResourceWatchTargets`としてロード中・公開中の入力ID集合を監視側へ渡します。各集合は不変で、次回取得時に最新となります。`ResourceReloadService`は通知をまとめ、IDの重複を除いてHotReloadします。連続更新で待機し続けないよう集約時間に上限を設け、再構築中に届いた通知も次のバッチへ保持します。保留できるID数を超えた場合や`ResourceSourceChange.Rescan`を受信した場合は、追跡中の入力をすべて再確認します。

`Conflict`と構築・入力読み取りの失敗は、設定された間隔・上限回数で再試行します。失敗時は旧公開値を維持し、再試行を使い切った後も新しい通知は受け付けます。`LastResult`・`LastError`と`Reloaded`・`ReloadFailed`で経過を確認でき、監視列挙自体の失敗は`Completion`へ伝わります。イベントはバックグラウンド処理上で実行されるため、UIなどへの配送は利用側で行います。

DIでは`UseSource<TSource>()`だけで監視も接続します。Managerは終了時に監視と進行中の自動再構築をキャンセルしてjoinしてからリソースを解放します。HotReload専用の`Start`／`StopAsync`は不要ですが、ExecutionRuntimeは先に起動し、Managerの解放後に停止します。`manager.HotReload`から結果・失敗・`Completion`を観測できます。通知元はSourceに属し、Managerは購読の寿命を管理します。FileResourceSourceは読み込み元と同じrootの監視を提供します。`ConfigureHotReload(new() { Enabled = false })`で購読を無効にでき、DIを使わない場合はconstructorの`reloadOptions`へ設定します。複合SourceではStreamの振り分けと同じ規則で監視対象を振り分けてください。

## ストリーミング

長い音声などは不変の`IResourceStream<TElement>` factoryをPipeline出力にします。各sessionが独立したreader、cursor、固定個数の固定長bufferを所有し、先読みが満杯になるとproducerを停止します。readerの短いreadに対応し、非空readの0は終端として扱います。decoder固有の作業領域もsessionのmemory scopeへ計上してください。

`ReadAvailable`は待機・I/O・デコードを行わず、準備済みの要素だけを返します。blockの解放時には再利用するwork itemでproducerを通知し、readerの継続をconsumer上でinline実行しません。通知ごとのTaskやdelegateは生成しませんが、ThreadPool内部の初期化・queue拡張まで無割り当てとは保証しません。0はunderflowにもなるため`EndOfStream`と区別します。producerの満杯待機は通知方式、`ReadAsync`のunderflow待機は1 msの非同期pollです。デバイスcallbackでは`ReadAvailable`を使い、consumerは一つとしてreadとseekを並行させないでください。

readのキャンセルは待機だけを中断します。seekは旧先読みを終了してからreaderを移動し、失敗するとsessionはfaultになります。sessionの終了はproducer、reader、bufferとmemory scope、factory leaseの順です。終了失敗時は所有を保持して再試行できます。作成失敗の片付けが失敗した場合はmanagerの`TrimAsync`で再試行します。HotReload後も既存sessionは旧factoryを保持し、新規sessionは新factoryを使用します。直接読み込みの入力が変更された場合、既存readerはIOExceptionで失敗してsessionがfaultになるため、新しいsessionを開き直してください。準備済みbufferの存在は旧ファイル全体の読み込み継続を保証しません。

コーデックやAudio backendへの再生接続は形式・機能側で実装します。このパッケージはデコード済み要素を受け渡す境界までを提供します。

## 終了と同期

外部reference・lease・resource snapshot・sessionや進行中の構築がある場合、managerの終了は状態を変えず拒否します。利用権を返し、managerの終了成功後にDI scopeを閉じてください。managerは借用したSource・Stage・各Runtimeを解放しません。ExecutionRuntimeはmanagerの解放成功後に停止します。終了途中のcleanup失敗は`DisposeAsync`の再呼び出しで再試行します。

公開mapと所有数の更新は短い同期区間で行い、ステージ、I/O、解放callbackをその区間で実行しません。Memoryの取得・返却は既存のスレッド専用チャンクを使います。Stageに選択したRuntimeが共通ジョブ環境とowner配送を調整します。同じworkerへの復帰は保証しないため、GPUやAudio固有の配送・完了待ちは各モジュールのAPIで行います。

具体型の子への参照を持つ複合リソース、二つの型へのprojection、handleとHotReloadを組み合わせた実行例は[sample](../../../samples/Lumyte.Resources.Sample/README.md)を参照してください。

外部glTF・BIN・PNGからCPU→GPU変換を経て深度テスト付きで3D描画する実行例は、[glTF sample](../../../samples/Lumyte.Resources.Gltf.Sample/README.md)を参照してください。画像ファイルだけの変更による自動HotReloadと、旧GPU世代を保持した再描画も検証します。

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

## Runtimeと非同期ロード

`IResourceRuntime`はStageが必要とする実行先・device・owner・世代の借用をまとめるリソース環境です。
ResourceManagerは出力型の対応を固定し、選択したRuntimeを各内容世代に保存してBuildAsyncとReleaseAsyncへ使います。
Stageは`ResourceBuildContext.Runtime`で選択された安定した環境を取得します。
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
