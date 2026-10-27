# Lumyte.Resources

出力型を識別子とするPipelineで、共有読み込みと依存を管理します。Graphics・Audio・Platformの実装には依存しません。
`RetainAsync<T>(ResourceKey<T>)`は論理リソースを保持するReferenceを返します。
必要なら初回ロードと構築を待ち、その後はHotReloadによる公開内容の差し替えを追従します。`TryRetain(handle, out reference)`はI/Oなしで論理所有を増やします。
描画開始など値を固定したい時点でReference.Pin()から値とRevision付きのLeaseを取得します。
取得APIは`RetainAsync`・`TryRetain`と`Pin`・`TryPin`に統一します。
`ResourceSnapshot.Pin(handle)`・`TryPin(handle, out lease)`は捕捉した公開状態からLeaseを取得します。
`Pin`は内容世代を固定する操作です。GCアドレスの固定や値の複製は行わず、デバイスロストなどによるruntime失効も防ぎません。
Try取得は成功時の出力が非nullと注釈されているため、成功分岐でnull抑制は不要です。
名前と所有権の対応は[ADR-RESOURCES-0009](../../../docs/adr/resources/RESOURCES-0009-resource-acquisition-names.md)を参照してください。
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

## Pipelineと依存

`ResourceKey<T>`は`ResourceAddress`と`T`本体の組み合わせです。Pipelineの登録・検索、内部キー、依存解決には`typeof(T).TypeHandle`を使い、型名の文字列を識別子にしません。manager内では出力型ごとに一つのPipelineを登録し、transformの入力も`TInput`の型から解決します。`Document → CpuMesh → GpuMesh`のようにCPU・GPUや意味が異なる出力はそれぞれの型で表現します。

AddressのSubresourceで単一ファイル内の要素、Variantで同じ型の変換条件を選びます。閉じたジェネリック型や、別のAssemblyLoadContextに読み込まれた同名型も、それぞれ別の型として扱います。Pipelineの型handleはプロセス内の識別用で、ファイルへの保存や通信には使いません。複合リソースは具体型ごとに子への参照またはResourceHandleを保持します。具体的な子の借用値を保持する場合はResident依存を記録し、子の寿命を親まで延長します。

```csharp
pipelines
    .AddSourcePipeline<SceneDocument, DocumentStage>()
    .AddTransformPipeline<SceneDocument, CpuMesh, MeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, UploadStage>();

var key = new ResourceKey<CpuMesh>("scene.json", "mesh/0");
using ResourceReference<CpuMesh> reference = await resources.RetainAsync(key);
using ResourceLease<CpuMesh> mesh = reference.Pin();
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
using ResourceReference<MyResource> reference = await manager.RetainAsync(
    new ResourceKey<MyResource>("asset.bin"));
// ファイル編集などの通知で、Managerが必要な依存と親だけを再構築する。
using ResourceLease<MyResource> frame = reference.Pin();
```

`IResourceSource.Changes`が通知元を返すと、Managerがその監視を自動開始します。Streamを開く取得元と監視先の対応はSourceが保証します。Changesの既定値はnullで、不変なDLL埋め込みなどでは監視を開始しません。Managerは`IResourceWatchTargets`としてロード中・公開中の入力ID集合を監視側へ渡します。各集合は不変で、次回取得時に最新となります。監視無効時はこの集合を管理しません。初回公開では新しく追加された入力だけを再確認し、既存入力の全走査を避けます。Manager内部の監視処理は通知をまとめ、IDの重複を除いてHotReloadします。連続更新で待機し続けないよう集約時間に上限を設け、再構築中に届いた通知も次のバッチへ保持します。保留できるID数を超えた場合や`ResourceSourceChange.Rescan`を受信した場合は、追跡中の入力をすべて再確認します。

`Conflict`と構築・入力読み取りの失敗は、設定された間隔・上限回数で再試行します。失敗時は旧公開値を維持し、再試行を使い切った後も新しい通知は受け付けます。`LastReloadResult`・`LastReloadError`と`Reloaded`・`ReloadFailed`で経過を確認でき、監視列挙自体の失敗は`HotReloadCompletion`へ伝わります。イベントはバックグラウンド処理上で実行されるため、UIなどへの配送は利用側で行います。

DIでは`UseSource<TSource>()`だけで監視も接続します。Managerは終了時に監視と進行中の自動再構築をキャンセルしてjoinしてからリソースを解放します。HotReload専用の`Start`／`StopAsync`は不要ですが、ExecutionRuntimeは先に起動し、Managerの解放後に停止します。Managerの`Reloaded`／`ReloadFailed`、`LastReloadResult`／`LastReloadError`、`HotReloadCompletion`から結果と障害を観測できます。監視サービスは内部実装で、生成・起動・停止をManagerが管理します。`IsHotReloadEnabled`は監視の構成有無を示します。通知元はSourceに属し、Managerは購読の寿命を管理します。FileResourceSourceは読み込み元と同じrootの監視を提供します。`ConfigureHotReload(new() { Enabled = false })`で購読を無効にでき、DIを使わない場合はconstructorの`reloadOptions`へ設定します。複合SourceではStreamの振り分けと同じ規則で監視対象を振り分けてください。

## ストリーミング

OpenStreamAsyncは不変factoryを保持し、独立したreaderとboundedな先読みbufferを作ります。
factoryの保持は旧入力ファイルの保存を意味しません。直接読み込み中の変更検出でsessionがfaultになった場合は開き直します。

readerの生成・先読み・seek・解放はfactoryの登録済みExecutorへ配送します。既定の`ResourceRuntime`ではJobSystemのCPU同時実行数に従い、Cooperativeでは`PumpJobs`で進めます。初期化・seek・解放の待機中もPumpを続けてください。producerはI/Oやbuffer空きを待つ間はCPU枠を返しますが、実完了までworkflowの受付枠を一つ保持します。長寿命sessionも含めて`MaxInFlightRuns`を設定し、すべてのsessionを閉じてから実行器を停止します。

`ReadAvailable`は待機・I/O・デコードを行わず、準備済みの要素だけを返します。blockを返すと再利用する通知でproducerをJobSystemへ戻し、readerをその呼び出しの中でinline実行しません。0はunderflowにもなるため`EndOfStream`と区別します。`ReadAsync`もデータ・終端・失敗の通知を待ち、timerでpollしません。デバイスcallbackでは`ReadAvailable`を使い、consumerは一つとしてread・seek・disposeを重ねないでください。

producer・reader・buffer/scope・factory leaseの順で終了します。取得済みsessionの解放に失敗した場合は、そのsessionの`DisposeAsync`を再度呼んでください。生成と後始末の両方が失敗し、利用者へ返せなかったsessionはManagerが保持し、`TrimAsync`で解放を再試行します。

[ADR-RESOURCES-0005](../../../docs/adr/resources/RESOURCES-0005-bounded-streaming.md)を参照してください。

readの取消は待機だけを中断します。seek開始後の失敗・取消ではsessionがfaultになります。HotReload後も既存sessionは旧factoryを保持し、新しいsessionが新factoryを取得します。decoder固有の作業領域もsessionのmemory scopeへ計上してください。

## 終了と同期

外部reference・lease・resource snapshot・sessionや進行中の構築がある場合、managerの終了は状態を変えず拒否します。利用権を返し、managerの終了成功後にDI scopeを閉じてください。managerは借用したSource・Stage・各Runtimeを解放しません。ExecutionRuntimeはmanagerの解放成功後に停止します。終了途中のcleanup失敗は`DisposeAsync`の再呼び出しで再試行します。

公開mapと所有数の更新は短い同期区間で行い、ステージ、I/O、解放callbackをその区間で実行しません。Memoryの取得・返却は既存のスレッド専用チャンクを使います。Stageに選択したRuntimeが共通ジョブ環境とowner配送を調整します。同じworkerへの復帰は保証しないため、GPUやAudio固有の配送・完了待ちは各モジュールのAPIで行います。

具体型の子への参照を持つ複合リソース、二つの型へのprojection、handleとHotReloadを組み合わせた実行例は[sample](../../../samples/Lumyte.Resources.Sample/README.md)を参照してください。

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
    using ResourceReference<Mesh> mesh = await manager.RetainAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Pin();
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

Managerの`RetainAsync`とhandleからの`TryRetain`はReferenceを返します。
Referenceを利用期間中保持し、描画・ジョブの開始時に`reference.Pin()`で具体的な値とRevisionを固定します。
複数リソースを一つの公開状態から取得する場合は`CaptureSnapshot()`で取得した`ResourceSnapshot`を`reference.Pin(snapshot)`へ渡します。
Lease取得はI/Oや構築を行いません。Reference返却後も、取得済みLeaseの値は利用できます。
HotReloadは新世代を別に公開するため、取得済みLeaseは取得時の値とRevisionを保持します。
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
