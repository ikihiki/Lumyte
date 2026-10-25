# ADR-RESOURCES-0003: リソース環境と非同期ロード

- 状態: 採用
- 日付: 2026-10-25
- 関連カテゴリ: core

## 背景

リソース構築は入力I/O、CPU変換、所有スレッド上の実体生成を組み合わせる。
Stageには必要なサービスと実行先を型付きで渡し、Managerにはその環境と使用した世代を把握させる必要がある。
環境の提供とジョブへの配送を同じ拡張メソッドにすると、ジョブを共有する子Stageだけ環境の準備が省略されるなど、実行経路で契約が変わる。
構築中の取消、共有依存、デバイス復旧でも所有を失わないよう、Stageの実行手順をManagerへ集約する。

## 決定

### 環境・実行器・構築状態

| 対象 | 責務 |
| --- | --- |
| `IResourceRuntime` | Stageが使う安定した環境。型付きサービス、実行器、環境依存を提供する |
| `IResourceExecutor` | Managerの構築手順と出力解放を実行先へ配送する |
| `ResourceManager` | 環境の登録・選択、構築・解放、内容世代、依存グラフ、検証と公開を管理する |
| `ResourceBuildContext` | 一回の構築で使う入力・メモリ・世代Lease・cleanupをManagerへ記録する |
| `ExecutionRuntime` | ジョブの開始・再開、CPU枠、所有スレッドへの配送を管理する |
| ホスト・device service | 共有スレッドと物理deviceの起動、復旧、退役、停止を所有する |

Runtimeはdevice交換をまたいで保持する。Stageへ生deviceを固定注入せず、構築ごとにRuntimeから現在の世代を借りる。
SourceとMemoryのサービスはManagerへ注入し、その使用記録をContext経由で管理する。共有Runtimeへ構築ごとの可変状態を持たせない。
Stage用のservice locatorや、毎回確保する汎用sessionは導入しない。

既定の`ResourceRuntime`は`Lumyte.Resources.Execution`に置き、既定環境と共通実行器を兼ねる。
Audio・Graphicsの環境はこの実行器を共有し、固有のdevice・owner操作を型付きメソッドとして提供する。
環境自身にBuild/Releaseの転送メソッドを実装させない。

```text
ResourceManager
  ├─ Stage → 選択したRuntime → 型付きサービス・環境依存
  └─ 登録時に固定したExecutor → workflow
                                 └─ Managerの構築手順 → Stage
```

### 環境の登録

出力型ごとの`ResourceStageRuntime`をManager構築時に固定し、指定のないStageには一つの既定Runtimeを使う。
同じRuntime instanceのExecutorとDependenciesは一度だけ取得し、以後の構築・解放には登録済みの実行器を使う。
Runtimeプロパティを後から変更して実行先や依存先を入れ替える契約にはしない。

Managerは選択した環境の`Dependencies`を収集し、明示的な追加providerと合わせて監視する。
CPUだけの環境は空の一覧を返せる。複数サービスを使う環境は複数providerを提示でき、Runtime自身がproviderを兼ねる必要はない。
同じprovider instanceの重複だけを除き、同じIdを持つ別instanceは拒否する。
登録は環境の利用記録ではない。使用した実世代とBuild/Resident寿命は構築ごとに記録する。

Stageは`IResourceSourceStage<T, TRuntime>`または`IResourceTransform<TInput, TOutput, TRuntime>`で環境型を宣言する。
Managerは宣言型と選択したinstanceの互換性を構築時に検証する。
BuildとReleaseの両方に型付きRuntimeを渡し、Stage側のキャストや解放用Runtime参照の重複を不要にする。
共通層は`ResourceBuildContext.Runtime`も利用できる。

```csharp
public sealed class MeshStage : IResourceTransform<CpuMesh, Mesh, MeshRuntime>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;
    public ResourceAddress GetInput(ResourceAddress output) => output;

    public ValueTask<Mesh> BuildAsync(CpuMesh input, MeshRuntime runtime,
        ResourceBuildContext context, CancellationToken cancellationToken)
        => runtime.CreateMeshAsync(input, context, cancellationToken);

    public ValueTask ReleaseAsync(Mesh resource, MeshRuntime runtime)
        => runtime.DestroyMeshAsync(resource);
}
```

MeshRuntime、Mesh、CpuMeshはアプリが定義する例である。
解放には生成時の実体・世代Lease・ownerを使う。渡されるRuntimeは安定した環境参照であり、現在のdeviceを取り直してよいという意味ではない。

### Managerが持つStage実行手順

ExecutorのBuildAsyncは、ジョブ内で`context.ExecuteAsync(token)`を呼ぶ。
この入口からManagerが次の手順を一貫して実行する。ジョブを共有する子Stageも同じ手順を通る。

1. 選択済みRuntimeを使ってStageを実行する。
2. 完成した出力をManagerの所有として記録し、Contextへの新しい登録を閉じる。
3. 登録済みの依存・Source操作が実際に完了するまで待つ。
4. Build寿命の所有を返し、構築完了を確定する。失敗時は出力も回収し、元の失敗と回収失敗を報告する。

ContextとManagerで終了手順を別々に実装しない。
親の待機が取消されても、共有依存が完了する前に継続先のジョブを閉じない。
Stageが依存取得の例外を処理しても、登録済み依存そのものの失敗を隠して公開しない。
ExecutorはBuildとReleaseを同じCPU schedulerへ配送する。失敗した構築の出力は、そのジョブ内でStageのReleaseを直接実行する。
ExecutorのReleaseはジョブ外からの配送入口とし、native操作は環境がownerへ送る。公平性yieldは共有ジョブ内の子Stageが完了通知を返す前に行い、依存の完了待ちに含める。
終了済みStageには新たなyieldを追加しない。キャッシュ取得など新しいStageを実行しない呼び出しは同期処理の一部とする。
公開は実完了とSource・環境世代の検証後に行い、取消要求だけで未回収の出力を捨てない。
共有ロードに対するAcquireの取消は呼出元の待機だけに適用し、Manager所有の構築取消とは分ける。

内部の構築・依存・Source完了は、状態更新と所有の記録を済ませてManager lockの外で通知する。
同じジョブなら直接継続でき、別ジョブの待機者は元の実行先へ戻す。外部Acquireへの最終通知は非同期に分離する。
登録の締切と完了待ちの対象決定を同じManager lockで行い、遅れて到着した登録を拒否する。
Stageは同期的なContext呼び出しをreturnまでに終え、終了後もContextを使い続ける仕事を残さない。
登録済み操作をすべて観測した後はManagerの完了Task参照を切り、結果に含まれる入力を保持し続けない。
同期的な補助所有物は`RegisterCleanup(IDisposable, lifetime)`で直接登録できる。成功済みcleanupは対象への参照も切り、失敗した対象だけを再試行用に保持する。

### 非同期実行とジョブの共有

既定Executorは構築の開始と通常のawait後の継続をworkflowジョブで実行する。
未構築の依存Stageが同じRuntime instanceを使い、同じ有効なジョブから要求された場合、そのジョブを共有する。
Runtimeが切り替わる境界と独立したAcquireは別ジョブにする。`A → B → A`は3区間であり、共通Executorを持つだけの別環境は統合しない。
キャッシュ済み・構築中の依存は実行を増やさず、元の結果を共有する。論理リソースの世代・依存・解放はStageごとに維持する。

ThreadPoolではworker、Cooperativeではホストの`PumpJobs`が同期区間を進める。同じ物理workerへ戻る保証はない。
I/O、依存Stage、ownerコマンドを待つ間はCPU枠を返すため、並列数1でも子の処理を進められる。
workflow内で個別の子仕事を送ってawaitできるが、同期Wait/Resultや再入Pumpは禁止する。
動的なロード依存グラフをExecutionのJobGraphへ変換しない。

StageとCPU処理を含む補助メソッドは通常のawaitを使う。
特に、待機後に次のownerコマンドを送る環境メソッドはworkflowの実行文脈を保つ。
`ConfigureAwait(false)`、Task.Run、独自スレッドへ移した処理はCPU並列度とPump予算の保証対象外となる。
Sourceやdevice service内部の終端処理ではConfigureAwait(false)を使えるが、後続のStageは通常のawaitでジョブへ戻る。

既定ExecutorのShouldYieldは`ExecutionRuntime.ShouldYield(TimeSpan)`を使い、現在の同期実行区間で約1ms経過したかを判断する。
I/Oやキューで待った時間は含めず、各再開時に計測を始め直す。Managerは依存Stageの境界でこの判断を使う。
深い同期呼び出しには別途32段でyieldしてstackを解放する。
実行中のStageを途中で中断する機構や実時間保証はない。大きなCPU処理はStage側で有限の区間へ分ける。

### 環境世代・失効・解放

`IResourceRuntimeDependency`のIdはManager内で一意、Identityは安定したservice identityとし、登録後に変更しない。
SnapshotのVersionとInvalidatedThroughVersionは単調増加させ、巻き戻しと周回を許さない。
状態が変わるたびにVersionを進め、利用可能なVersionは0より大きく失効watermarkを超える必要がある。
Changedはメタデータ更新の通知とし、handlerから同期的に再構築を呼ばない。
通知が未配送でも取得・参照時にSnapshotを再確認する。provider契約違反はManagerの失敗として保持する。

構築ごとに取得したLeaseの実際のVersionとprovider identityを記録する。
構築中は直接記録したVersionと現在のVersionの一致を要求し、一つのStageが同じIdの異なるVersionを直接混在させることを拒否する。
既存のResident子が使う有効な旧Versionと、親が直接使う新Versionはそれぞれ検証できる。

Build依存は構築時と公開前に検証し、完成した独立値はその後のBuild依存の喪失では失効しない。
Resident依存の利用不能は親にも伝播する。推移的なResident依存は構築完了時に平坦化し、値参照時の再帰探索を省く。
Versionが進んだだけでは旧出力を失効させず、利用不可またはwatermark以下のResident依存を使用不能にする。
watermarkによる失効は復旧後も戻さない。watermarkが進まない一時的な利用不可は、回復後に旧Leaseを再利用できる。

`RebuildRuntimeAsync`は指定providerを使う出力と依存する親を再構築し、Source Revision・環境世代・公開Versionを検証して候補mapを一括公開する。
競合はConflict、変更不要はUnchangedとし、失敗候補を部分公開しない。
既存Handleは新しい内容世代を指し、旧Leaseは元の内容世代を保持する。

出力を生成時のRuntimeと登録済みExecutorで解放してから、世代Leaseなどの補助cleanupを返す。
解放失敗では所有と進捗を保持し、TrimAsyncで未完了部分を再試行する。
環境の登録と利用の管理はManagerが担い、共有する物理device・workerの寿命はホストとdevice serviceが担う。

## 公開契約

主要な契約を示す。型付きStage、世代provider、DIの詳細なシグネチャは各公開型を参照する。

```csharp
public interface IResourceRuntime
{
    IResourceExecutor Executor { get; }
    IReadOnlyList<IResourceRuntimeDependency> Dependencies => [];
}

public interface IResourceExecutor
{
    bool ShouldYield => false;
    ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken);
    ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource);
}

public readonly record struct ResourceStageRuntime(Type OutputType, IResourceRuntime Runtime);

public interface IResourceRuntimeDependency
{
    string Id { get; }
    object Identity => this;
    ResourceRuntimeSnapshot Snapshot { get; }
    event EventHandler? Changed;
}

public readonly record struct ResourceRuntimeSnapshot(
    ulong Version, bool IsAvailable, ulong InvalidatedThroughVersion);
```

`ResourceBuildContext.ExecuteAsync(token)`はExecutorが一度呼ぶManager手順の入口であり、Stageが別のStageを直接実行するAPIではない。
StageはRequireAsync、OpenSourceAsync、GetMemoryScope、RegisterCleanupを通して依存と所有を記録する。
`TrackRuntimeDependency`は未登録IdをArgumentException、identity不一致・混在VersionをInvalidOperationException、利用不能・Version不一致・失効済みをResourceUnavailableExceptionで通知する。

## 登録・起動・終了

DIなしでは既定RuntimeをExecutionRuntimeのmodule集合とManagerへ渡す。
次のMeshRuntimeは共通Executorとdevice serviceを組み合わせるアプリ定義の環境である。
例はホストイベントループへPumpを接続済みであることを前提とする。device moduleも起動前に登録する。

```csharp
var resourceRuntime = new ResourceRuntime();
var meshRuntime = new MeshRuntime(resourceRuntime, deviceService);
var execution = new ExecutionRuntime([resourceRuntime, deviceModule]);
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

await using (var manager = new ResourceManager(
    "assets", source, [new DocumentStage(), new MeshStage()], memory, resourceRuntime,
    stageRuntimes: [new(typeof(Mesh), meshRuntime)]))
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await execution.StopAsync();
```

`AddResources`は既定ResourceRuntimeとExecutionRuntimeをsingleton登録し、既存の登録は上書きしない。
複数の名前付きManagerは同じ既定環境・実行器を借りる。
ResourceRuntimeのmodule登録はAddResourcesに任せ、同じinstanceを重複登録しない。
UseStageRuntimeは既存環境をManager構築時に解決する。環境のDependenciesとは独立した追加providerだけをUseRuntimeDependency等で登録する。

```csharp
services.AddSingleton<IMemoryManager>(memory);
services.AddSingleton<MeshRuntime>(); // 共通Runtimeとdevice serviceをconstructorで受け取る。
services.AddResources("assets", resources => resources
    .UseSource<MySource>()
    .AddSourcePipeline<Document, DocumentStage>()
    .AddTransformPipeline<Document, Mesh, MeshStage>()
    .UseStageRuntime<Mesh, MeshRuntime>());
ServiceProvider provider = services.BuildServiceProvider();
ExecutionRuntime execution = provider.GetRequiredService<ExecutionRuntime>();
execution.PrepareOnCurrentHost(
    OwnerPlacement.Host, CpuExecutionMode.ThreadPool, 2, new ExecutionOptions());
await execution.StartAsync();

AsyncServiceScope scope = provider.CreateAsyncScope();
ResourceManager manager = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("assets");
{
    using ResourceReference<Mesh> mesh = await manager.AcquireAsync(meshKey);
    using ResourceLease<Mesh> lease = mesh.Acquire();
    Consume(lease.Value);
}

await manager.DisposeAsync(); // 失敗時は同じManagerで再試行し、成功後にscopeを閉じる。
await scope.DisposeAsync();
await execution.StopAsync();
await provider.DisposeAsync();
```

DI解決だけでは起動せず、ホストがPrepare/Start/Pump/Stopを管理する。
BrowserはHost/Cooperativeに配置し、ロード・解放をawaitしている間もフレームループからPumpJobsと必要なHost phaseを進める。
Reference・Lease・Snapshotを返し、Managerの構築と解放を完了してから実行器・device環境を停止する。
解放の容量不足やbackend失敗ではManager・scope・Runtime・ExecutionRuntimeを保持して再試行し、DI scopeのDisposeAsyncの再試行に依存しない。
Managerは借用したSource・Stage・Runtime・Executor・providerそのものを破棄せず、終了時に通知購読と取得済みの所有を返す。

## 性能と制約

環境と実行器の対応は登録時に固定し、ロードごとのDI解決やRuntime生成を行わない。
構築区間の配送は型付きstructと既存Contextを使い、送信ごとのラムダや汎用sessionを追加しない。
同一環境の構築区間は一つのworkflowで進めるが、出力の解放は世代ごとの配送を維持する。
非同期状態機械、継続callback、依存グラフの確保は残り、無割り当ては保証しない。
構築viewは不変の公開mapを共有し、metadataは変更前に必要なコピーを作る。公開時は変更したslotだけを更新する。
Trimは同時に解放可能な世代を一度に公開mapから外し、親の解放で利用権が返った依存を次に処理する。取消で未実行の世代を回収不能な状態に残さない。
metadataの回収は変更候補から進め、残った参照の到達性を確定できない場合は全体の到達性検証を使う。

受付容量はRuntime区間ごとに予約し、実行器がworkflowの結果を一度観測して回収するまで保持する。
停止は実完了を待ち、未観測結果の回収そのものは待たない。開始済みの処理はCancelPendingでも継続を維持し、協調取消と実完了を待つ。
Lease検証では登録providerの状態を確認し、平坦なResident依存を走査するため、依存数に対する定数時間は保証しない。
再構築中は新旧世代のメモリが重なる。遅延分布とピークメモリは実アセットで評価する。

## 検討した代替案

- Runtime自身へBuild/Releaseの配送を実装させる: 環境の責務と区間単位の配送が混ざるため、Executorを明示してManagerの共通手順を呼ぶ。
- Stageごとに別ジョブへ配送する: 微小なStageの管理費が増えるため、同一環境の連続区間は同じジョブを使う。
- Managerが共有deviceやworkerを破棄する: 複数Managerの利用と競合するため、Managerは利用の所有を、ホストは物理環境の寿命を管理する。
- Stageへ生deviceを固定注入する: 復旧後の世代を取得できず、旧出力の解放先も曖昧になる。
- Versionの変化をすべて失効とする: 有効な旧Leaseまで利用不能にするため、不可逆失効はwatermarkで区別する。

## 検証方針

型付きBuild/Releaseへの元Runtimeの注入、型不一致の拒否、登録済みExecutorの固定、複合環境のprovider収集・重複・identityを確認する。
同一環境の深いPipelineを一つの受付枠で実行し、Runtime境界、別実行文脈からの要求、共有依存の取消・失敗、出力回収を確認する。
I/O待機中のCPU枠返却、再開時に待機時間を含めないyield判定、同期呼び出しの深度制限を検証する。
通知はlock外で行い、同じジョブの内部通知で不要なThreadPool中継を増やさないことを確認する。
環境のロスト・復旧、Build/Resident寿命、旧Lease、公開競合、解放失敗の再試行と終了順を検証する。

## 関連ADR

- [ハンドルと世代](RESOURCES-0002-generations-reload.md)
- [実行基盤](../core/CORE-0003-execution-and-jobs.md)
