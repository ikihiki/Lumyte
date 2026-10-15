# Lumyte.Resources

出力の型で識別するPipelineで、入力snapshot、CPU中間型、GPUなどの実行時出力を管理します。SourceとStageはconstructor injectionで構成し、登録は`Lumyte.Resources.DependencyInjection`の`AddResources`を使います。Memory以外のGraphics・Audio・Platform実装には依存しません。

## Pipelineと依存

`ResourceKey<T>`は`ResourceAddress`と`T`本体の組み合わせです。Pipelineの登録・検索、内部キー、依存解決には`typeof(T).TypeHandle`を使い、型名の文字列を識別子にしません。manager内では出力型ごとに一つのPipelineを登録し、transformの入力も`TInput`の型から解決します。`Document → CpuMesh → GpuMesh`のようにCPU・GPUや意味が異なる出力はそれぞれの型で表現します。

AddressのSubresourceで単一ファイル内の要素、Variantで同じ型の変換条件を選びます。閉じたジェネリック型や、別のAssemblyLoadContextに読み込まれた同名型も、それぞれ別の型として扱います。`IResourceDocument.Subresources`は発見用のmetadataだけを返し、`OutputTypes`に取得可能な出力型の`RuntimeTypeHandle`を保持します。これらのhandleはプロセス内の識別用で、ファイルへの保存や通信には使いません。

```csharp
pipelines
    .AddSourcePipeline<SceneDocument, DocumentStage>()
    .AddTransformPipeline<SceneDocument, CpuMesh, MeshStage>()
    .AddTransformPipeline<CpuMesh, GpuMesh, UploadStage>();

var key = new ResourceKey<CpuMesh>("scene.json", "mesh/0");
using ResourceLease<CpuMesh> mesh = await resources.AcquireAsync(key);
```

通常はパス文字列から直接キーを作成します。SubresourceとVariantは省略でき、`new ResourceKey<SceneDocument>("scene.json")`や`new ResourceKey<CpuMesh>("scene.json", "mesh/0", variant: "geometry:v1")`と記述できます。既存の`ResourceId`または`ResourceAddress`を渡すこともできます。

ステージは`ResourceBuildContext.RequireAsync`で依存を要求します。互換な構築view内の同じキーを一つの構築へ集約し、別々に開始した構築間の循環も検出します。入力そのものを別の所有出力として返すことはできません。値は共有されるため、利用中は読み取り専用として扱います。

`Build`依存は構築とその片付けまで、`Resident`依存は出力の解放完了まで保持します。同じ依存を両方の寿命で要求した場合はResidentへ引き上げます。contextの操作はステージ内で完了までawaitし、ステージからmanagerを直接呼んで依存を隠さないでください。

ファイルを読む操作も`OpenSourceAsync`を経由します。snapshotは一つの入力Revisionに固定され、contextが所有します。ステージが開いたstreamはステージが閉じます。具体的な取得元には`Lumyte.Resources.FileSystem`を使用できます。

DLLの埋め込みデータには`Lumyte.Resources.Embedded`の`EmbeddedResourceSource`を使用できます。Assemblyと必要に応じて論理ID・manifest resource名の対応をDIで渡し、同じPipelineから読み込みます。埋め込みデータの範囲読み込みも可能です。

## 所有権とメモリ

- `ResourceLease<T>`は内容Revisionを固定し、返却はI/OやGPU待機を行いません。
- `ResourceReference<T>`は論理キーを常駐させ、HotReload後は新しい公開世代を保持します。
- referenceの`ResourceHandle<T>`は非所有のunmanagedな16 byteの値で、ECSへ格納できます。最後のreference返却で失効し、slot再利用時はGenerationが変わります。
- `BeginRead`は一つの公開mapを保持します。フレーム内の複数handleをそのscopeで解決すると、新旧の組み合わせが変わりません。scope終了後も発行済みleaseは有効です。

利用者は値を直接Disposeせず、利用終了までleaseを保持します。handle自体のコピーは所有数を増やしません。unmanagedジョブへ渡すデータはmanaged側でhandleを解決して用意し、ジョブやGPU使用の完了までleaseを保持してください。

ステージはcontextからBuild用・Resident用の`IMemoryScope`を借ります。CPUメモリは`RentAsync`、Native資源は予約・物理確保・commitの順で計上します。出力の`ReleaseAsync`、補助cleanup、snapshot、memory scope、依存利用権の順で解放します。失敗した処理と後続の所有物は保持し、`TrimAsync`で再試行します。成功したcleanupは再実行しません。予約失敗からの自動回収は行いません。

`TrimAsync`は未使用の親から解放し、その結果未使用になった依存も同じ呼び出しで回収します。managerはMemoryのreclaimerへ登録されます。返却byte数は共有poolの累積解放カウンターの差分で、チャンク全体の解放と並行した返却を含みます。個々のsliceサイズを合計する方式ではありません。

## HotReloadとruntime世代

`ReloadAsync`は変更通知を入力Revisionの再確認のきっかけとして使い、逆依存をたどって候補を構築します。Build依存の値をTrimした後も、親が残る間は再構築用の依存情報を保持します。候補の構築・入力確認がすべて成功した後、値とhandleの解決先を一括公開します。途中で入力や公開mapが変わった場合は`Conflict`になり、候補を片付けます。構築失敗では例外を返し、旧公開値を維持します。

通常の新規取得でもsourceの変更を検出した場合、旧親と新しい中間値だけが混在する公開を拒否します。変更したIDを`ReloadAsync`へ渡してから取得を再試行してください。既存の公開値への取得は、Reloadが公開するまでその世代を使用します。通知されたIDのうち追跡中の入力だけを再確認し、未ロードの無関係なファイルは読み込みません。`ReloadAllAsync`は同じ構築viewに含まれるすべての入力Revisionを再確認します。

既存lease・read scope・stream sessionは旧Revisionを保持します。runtime依存の不可逆な失効は例外で、Resident依存する値とその親を利用不能にします。論理referenceとhandleは維持し、復旧後の`RebuildRuntimeAsync`で新世代へ接続します。Buildだけでruntimeを使った独立CPU値は利用を継続できます。Graphicsとの接続は`Lumyte.Resources.Graphics`を使用します。

## 自動変更監視

`IResourceChangeSource`は非同期の変更通知を返します。`ResourceReloadService`を開始すると通知をまとめ、IDの重複を除いてHotReloadします。連続更新で待機し続けないよう集約時間に上限を設け、再構築中に届いた通知も次のバッチへ保持します。保留できるID数を超えた場合や`ResourceSourceChange.Rescan`を受信した場合は、追跡中の入力をすべて再確認します。

`Conflict`と構築・入力読み取りの失敗は、設定された間隔・上限回数で再試行します。失敗時は旧公開値を維持し、再試行を使い切った後も新しい通知は受け付けます。`LastResult`・`LastError`と`Reloaded`・`ReloadFailed`で経過を確認でき、監視列挙自体の失敗は`Completion`へ伝わります。イベントはバックグラウンド処理上で実行されるため、UIなどへの配送は利用側で行います。

DIでは`WatchChanges<TChangeSource>()`を登録し、同じ名前の`ResourceReloadService`を取得して`Start()`します。`StopAsync`は監視と再構築をキャンセルし、終了まで待ちます。DI scopeも監視サービスをmanagerより先に停止します。managerを明示的に閉じる場合は、その前に監視を停止してください。ファイル監視には`Lumyte.Resources.FileSystem`の`FileResourceChangeSource`を使用します。

## ストリーミング

長い音声などは不変の`IResourceStream<TElement>` factoryをPipeline出力にします。各sessionが独立したreader、cursor、固定個数の固定長bufferを所有し、先読みが満杯になるとproducerを停止します。readerの短いreadに対応し、非空readの0は終端として扱います。decoder固有の作業領域もsessionのmemory scopeへ計上してください。

`ReadAvailable`は定常状態で待機・I/O・デコード・割り当て・lockを行わず、準備済みの要素だけを返します。0はunderflowにもなるため`EndOfStream`と区別します。最初の実装ではproducerの再開待ちと非同期readの準備待ちを1 msの非同期pollで行います。consumerは一つとし、readとseekを並行させないでください。

readのキャンセルは待機だけを中断します。seekは旧先読みを終了してからreaderを移動し、失敗するとsessionはfaultになります。sessionの終了はproducer、reader、bufferとmemory scope、factory leaseの順です。終了失敗時は所有を保持して再試行できます。作成失敗の片付けが失敗した場合はmanagerの`TrimAsync`で再試行します。HotReload後も既存sessionは旧入力を使い続け、新規sessionだけが新Revisionを使います。

コーデックやAudio backendへの再生接続は形式・機能側で実装します。このパッケージはデコード済み要素を受け渡す境界までを提供します。

## 終了と同期

外部reference・lease・read scope・sessionや進行中の構築がある場合、managerの終了は状態を変えず拒否します。利用権を返し、managerの終了成功後にDI scopeを閉じてください。managerは借用したSource・Stageを解放しません。終了途中のcleanup失敗は`DisposeAsync`の再呼び出しで再試行します。

公開mapと所有数の更新は短い同期区間で行い、ステージ、I/O、解放callbackをその区間で実行しません。Memoryの取得・返却は既存のスレッド専用チャンクを使います。実行スレッドは保証しないため、GPUやAudio固有の配送・完了待ちはステージ側で行います。

複合document、二つの型へのprojection、handleとHotReloadを組み合わせた実行例は[sample](../../../samples/Lumyte.Resources.Sample/README.md)を参照してください。

外部glTF・BIN・PNGからCPU→GPU変換を経て深度テスト付きで3D描画する実行例は、[glTF sample](../../../samples/Lumyte.Resources.Gltf.Sample/README.md)を参照してください。画像ファイルだけの変更による自動HotReloadと、旧GPU世代を保持した再描画も検証します。
