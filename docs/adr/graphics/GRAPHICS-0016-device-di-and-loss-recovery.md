# ADR-GRAPHICS-0016: DIによるデバイス世代の提供とDeviceLost後の再生成

- 状態: 提案
- 日付: 2026-10-10
- 更新日: 2026-10-12
- 関連カテゴリ: resources、memory

## 背景

GPUステージに生成済みIGraphicDeviceを直接注入すると、DI scopeが続く間は同じdeviceを使い続ける。device lost後に新しいdeviceへ変えても、ステージや既存GPU資源に残る参照は更新できない。CPUリソースや再生を維持しながら、GPUだけを新世代へ再構築する境界が必要である。

[GRAPHICS-0001](GRAPHICS-0001-graphics-device.md) のbackend具象device、固有の初期化条件、[GRAPHICS-0007](GRAPHICS-0007-command-buffers-and-submission.md) の明示的なsubmit／待機を維持し、DIとlost時の寿命を追加する。本ADRは共通の健康なdeviceの所有方式を置換せず、失ったdeviceに適用する追加契約を提案する。

## 決定

### 配置とDI境界

backend具象型はIGraphicDeviceを直接実装する。Status・loss通知・lost後の退役確認をGraphics.Abstractionsへ追加する。backendのNative APIをforwardする共通device wrapperは作らない。

各backendが共通して使う保存済み状態とloss通知の実装は `Lumyte.Graphics.Shared` の `GraphicsDeviceLifetime` に置く。backendはSharedの公開APIを直接使用し、assembly間の内部公開は行わない。

DI登録・async初期化・世代の所有を `src/Graphics/Lumyte.Graphics.Hosting/`、名前空間・パッケージ名 `Lumyte.Graphics.Hosting` に置く。Hostingは標準DIとAbstractionsに依存するが、特定backendやResourcesには依存しない。

backendまたはアプリがIGraphicsDeviceFactoryをDI登録する。factoryのconstructorでbackend固有のoptions、adapter選択、表示先、必要なdispatcherを受け取る。共通DeviceDesc、backend列挙、自動backend選択をAbstractionsへ追加しない。factoryは固有生成APIを使い、具体的な所有者をIGraphicsDeviceOwnerとして返す。ownerは寿命だけを包み、Deviceは実際のbackend instanceである。

DIへ登録する安定した入口はIGraphicsDeviceServiceとする。通常の1デバイス構成では名前なしで登録し、型で解決する。既定の寿命はScopedとし、アプリ全体で共有する場合はSingletonを指定できる。GPUステージやrendererはそのserviceをconstructor injectionで受け取り、使用する世代のGraphicsDeviceLeaseを取得する。生のIGraphicDeviceを更新可能なsingleton aliasとして登録しない。factoryとserviceは同じ寿命で登録し、その寿命以上の依存を使用する。device世代のownerはserviceが明示的に所有する。

InitializeAsyncはDI解決後にEngineがawaitする。DI factoryやconstructorでasync生成を同期化しない。GPUを使用しないworldにはserviceを登録しなくてよい。複数contextを明示的に選ぶ場合だけkeyed DIで名前を付ける。名前なしと名前付きのservice・factoryは独立して所有し、登録順や暗黙のaliasで既定deviceを変更しない。Resourcesとの接続は名前なしのserviceを既定とし、名前付きdeviceも同じ登録名で選択できる。

### serviceとepoch

serviceは不変のState snapshotを公開する。Nameは名前なしの登録で空文字列、名前付きでは指定した非空の名前とし、全世代で維持する。Versionは初期化・lost・復旧などの状態遷移ごとに増え、Epochはdevice instanceごとに増える。Readyは新しいdeviceが使用可能という意味であり、GPUアセットの復元完了を表さない。

AcquireAsyncはReadyのdeviceとStateを同じ同期境界で捕捉する。初期化中・Lost・Recovering・FailedではGraphicsDeviceUnavailableExceptionを返し、暗黙の再生成や無期限待機を行わない。取得済みleaseは同じinstanceを保持し、service.Currentへ読み替えない。Releaseでも必ず生成に使った旧deviceと実行contextを使う。

Initialize／Recoverの同時呼び出しは一つの操作を待つ。呼び出し側のキャンセルはその待機だけを解除し、他者の共有生成を中断しない。serviceは終了用の内部tokenを生成処理へ渡す。factoryは中止や失敗時に部分生成物を片付け、責任を失わない。

lostしたReadyのVersionをInvalidatedThroughVersionへ記録し、以後その値を減らさない。新deviceのReady Versionはこの値より大きくする。backendのlostを観測した時点でまずメタデータを失効させ、Native callbackからDI構築や再生成・GPU uploadを直接呼ばない。handlerは軽い通知に限定する。

### backendのlost契約

Nativeのdevice lost通知・errorを観測したらReadyからLostへ一度だけ移る。通常のvalidation失敗や回復可能な確保失敗をlostとして扱わない。Status・Lossは診断と上位の制御に使い、backend APIの操作可否を検証しない。loss後の操作停止はEngineが保証する。

Pending submissionはFailed、関連commandはFaultedへ移し、WaitAsyncはloss原因を含む例外で終了する。成功したGPU実行として扱わず、失ったfenceを永遠に待たない。未完了mapなどのasync操作も失敗として終了させる。保存済みSpanやNative pointerを後から回収できる保証はないため、Engineはloss通知時に利用中のCPU job・mapped領域へのアクセスも止める。

WaitForRetirementAsyncはlost backendがGPU使用終了または安全なNative破棄を確定する明示的な操作である。単なるLost flagやタイムアウトをその証明にしない。確定できない場合は失敗を返し、CPU ownerとメモリ計上を保持する。

低レイヤーbackendは子資源の操作やDisposeごとにdevice状態を検証しない。Engineがloss通知後にGPU jobと新規操作を止め、WaitForRetirementAsyncで安全性を確認してからregistration、View、子、deviceの順に解放する。Disposeにsubmit、完了待機、退役確認を隠さない。

Vulkanなどのfatal結果と、WebGPU系のloss通知を同じ共通状態へ接続する。通知が来ないbackendを実装済みのlost対応とみなさず、固有の観測・安全な破棄手順はbackend READMEで説明する。

### 公開API案

比較元は `main` の `586a380`。既存IGraphicDeviceの追加メンバーと、新規Hosting契約を示す。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // 保存済み状態。Native queryやGPU workをこのgetterで行わない。
+        GraphicsDeviceStatus Status { get; }
+        GraphicsDeviceLoss? Loss { get; }
+        // 一度のlossを通知。subscribe後にもStatusを確認して取りこぼしを避ける。
+        event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;
+        // lost後の安全な破棄を確認する。健康なdeviceへの呼び出しは状態違反。
+        ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default);
     }
+    public enum GraphicsDeviceStatus { Ready, Lost, Disposed }
+    public sealed record GraphicsDeviceLoss(string Reason, string Message);
+    public sealed class GraphicsDeviceLostEventArgs : EventArgs
+    {
+        public GraphicsDeviceLoss Loss { get; }
+    }
+    public sealed class GraphicsDeviceLostException : InvalidOperationException
+    {
+        public GraphicsDeviceLoss Loss { get; }
+    }
 }
+namespace Lumyte.Graphics.Hosting
+{
+    public enum GraphicsDeviceServiceStatus { Uninitialized, Initializing, Ready, Lost, Recovering, Failed, Disposed }
+    public sealed record GraphicsDeviceState
+    {
+        // 名前なしの登録では空文字列。
+        public required string Name { get; init; }
+        public required ulong Version { get; init; }
+        public required ulong Epoch { get; init; }
+        public required GraphicsDeviceServiceStatus Status { get; init; }
+        public required ulong InvalidatedThroughVersion { get; init; }
+    }
+    public interface IGraphicsDeviceOwner : IAsyncDisposable
+    {
+        // backendが直接実装するinstance。ownerはAPI forwardingをしない。
+        IGraphicDevice Device { get; }
+    }
+    public interface IGraphicsDeviceFactory
+    {
+        // backend固有設定はconstructor injection。生成物の所有権をserviceへ渡す。
+        ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class GraphicsDeviceLease : IDisposable
+    {
+        public GraphicsDeviceState State { get; }
+        public bool IsUsable { get; }
+        // 同じinstance。loss後はGraphicsDeviceLostException、返却後はObjectDisposedException。
+        public IGraphicDevice Device { get; }
+        // epochの利用権だけを返す。冪等。Native待機・destroyを行わない。
+        public void Dispose();
+    }
+    public interface IGraphicsDeviceService : IAsyncDisposable
+    {
+        public GraphicsDeviceState State { get; }
+        public event EventHandler? StateChanged;
+        ValueTask InitializeAsync(CancellationToken cancellationToken = default);
+        ValueTask<GraphicsDeviceLease> AcquireAsync(CancellationToken cancellationToken = default);
+        // Lost／Failedから再生成。新deviceがReadyになるまでを担当する。
+        ValueTask<GraphicsDeviceState> RecoverAsync(CancellationToken cancellationToken = default);
+        // LostのepochについてNative使用終了を確認する。利用権が残っていても呼べる。
+        // ownerや子は破棄しない。未知／健康なepochは引数例外。キャンセルは当該待機のみ。
+        ValueTask WaitForRetirementAsync(ulong epoch, CancellationToken cancellationToken = default);
+        // 旧ownerの明示的な退役。利用権や子が残るownerは保持する。
+        ValueTask<int> RetireAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class GraphicsDeviceUnavailableException : InvalidOperationException
+    {
+        public GraphicsDeviceState State { get; }
+    }
+    public static class GraphicsHostingServiceCollectionExtensions
+    {
+        // 名前なしのserviceとfactoryを同じ寿命で登録。重複する既定deviceは構成エラー。
+        public static IServiceCollection AddGraphicsDevice<TFactory>(this IServiceCollection services,
+            ServiceLifetime lifetime = ServiceLifetime.Scoped)
+            where TFactory : class, IGraphicsDeviceFactory;
+        // 複数context向け。named/keyed serviceとfactoryを登録。空の名前と重複名は構成エラー。
+        public static IServiceCollection AddGraphicsDevice<TFactory>(this IServiceCollection services, string name,
+            ServiceLifetime lifetime = ServiceLifetime.Scoped)
+            where TFactory : class, IGraphicsDeviceFactory;
+    }
+}
```

VersionとEpochは周回・再利用させず、枯渇した場合は状態更新・生成を失敗させる。通知前に状態を確定し、購読者の例外は報告して他の購読者への通知を継続する。例外をNative callbackへ伝播させない。

状態getter、State捕捉、lossメタデータの更新は並列通知を扱うため同期するが、IGraphicDeviceの一般操作がthread-safeになる契約ではない。factoryとGPUステージはbackend固有の実行スレッドへ配送し、旧ownerのcleanupにもその実行contextを保持する。backendの具象deviceがIDisposableを持つ場合、ownerがその固有契約へDisposeAsyncを接続する。

### 上位システムとの復旧手順

Engineはloss通知で描画・GPU job・mappedアクセスを停止する。取得済みleaseは同じdeviceを保持するが、IsUsableがfalseとなりDeviceへのアクセスを拒否する。CPUデータや音声などの独立した処理は継続できる。

service.WaitForRetirementAsync(旧epoch)でNative使用終了を確認し、registration・View・子資源・command・submissionを明示的に解放してから旧leaseを返す。RetireAsyncは未使用ownerを解放し、cleanup失敗時は責任を保持して再試行する。DisposeAsyncは生成中やlease保持中には終了を開始せず拒否する。終了開始後のcleanup失敗は新規利用を拒否したままDisposeAsyncで再試行する。

RecoverAsyncは新deviceをReadyにするまでを担当する。アセットの再upload、Capsの互換性検証、GPUグラフの一括公開、描画の再開は上位システムの責任であり、このPRにリソース／メモリ管理の実装を含めない。旧確保のメモリ計上は物理解放完了まで上位側が保持する。新deviceがReadyでもアセット復元前に描画を再開しない。

### 今回の実装とbackendの確認境界

Lumyte.Graphics.Hostingは名前なし／名前付きのDI登録（既定Scoped、任意Singleton）、constructor injectionによるfactory、共有生成、待機単位のキャンセル、epoch lease、失効watermark、明示的な退役・cleanup再試行を実装する。factory固有のNative生成と配送はIGraphicsDeviceFactoryの実装側が担当する。DI constructorでasync生成を同期ブロックしない。

Vulkanはnative ResultとpresentationのDeviceLostを観測し、未完了submissionをFailedへ移す。明示的なDeviceWaitIdleがSuccessまたはErrorDeviceLostを返した後、Vulkanのlost deviceに対する破棄規則に従って退役を確認する。これはNative利用の終了確認であり、失敗した提出を成功へ変換しない。

BrowserはGPUDevice.lostの解決で失効を通知し、queue完了待機もlossと競合させる。lost解決がWebGPUの破棄安全性の境界である。Interop観測失敗は利用を拒否するが、安全な退役を証明したことにはしない。

WgpuはAhjo.Wgpu 0.7.0が公開しないdeviceLostCallbackInfoをNative生成時に登録する。この版のDevice内部constructorへのUnsafeAccessorはWgpuDeviceCreationへ隔離し、依存更新時にはNative生成・lossテストを必須にする。wgpu-nativeで未実装のGetLostFuture／WaitAnyは使用しない。ProcessEventsをEngineが定期的に呼び、loss callbackを配送する。API操作とsubmission・map待機もeventをpumpする。Status／Loss getterは保存済み状態だけを返す。

Wgpuのmap／queue callbackが参照するNative status slotは、callback完了まで所有する。loss通知だけでslotを解放しない。退役確認は未完了requestのcallbackを明示的にdrainし、失敗／キャンセル時は責任を保持する。これらのNative操作の並列実行は従来どおり呼び出し側が同期する。

## 検討した代替案

- 生のIGraphicDeviceをDI singletonにする: 注入済み参照を復旧時に交換できない。安定した世代serviceを入口にする。
- providerが新しいIGraphicDeviceへ全操作をforwardする: 旧資源と新deviceの組み合わせを隠す。取得時にepochを固定する。
- lost後も旧lease.Valueを通常のHotReloadと同じように返す: 物理的なGPU使用不能を保持期間では解決できない。使用可否と解放責任を分ける。
- lost通知で子とdeviceを強制破棄する: Native使用・CPU pointer・メモリ計上の責任が失われる。明示的な退役確認と子からの解放を必要とする。

## 結果と影響

- 利用側に合わせたscopeまたはアプリ寿命でdeviceを構成し、ステージを再生成せず新epochを取得できる。singletonのResourceManagerへ接続する場合はSingletonを選択する。
- 各backendにloss検出、async失敗終了、安全な破棄の契約を実装する必要がある。
- GPUへの依存だけを失効・再構築できるが、rendererの停止、旧利用権返却、再開はEngineの責任になる。
- lostした旧値は使用できず、解放や復旧が失敗した責任と計上は保持する。

## 検証方針

fake factoryでasync DI初期化、名前なし／名前付きdeviceの独立した所有、scopeとsingletonの寿命、登録順によらない選択、同時Recover、生成失敗・キャンセル、epochとVersionの非再利用を確認する。重複する既定device・名前、空の名前、Transientを拒否する。構築中・submit中・map中のlost、Pending待機の終了、退役確認失敗、旧子のcleanupとcharge保持、CPU継続、新Capsの不一致、復旧中の再lost、新GPUグラフ公開前の描画再開拒否を検証する。backend別のNative lossと破棄安全性は統合テストで確認する。

## 別途決定する事項

- 各backendのfactory登録API、固有optionsと実行dispatcher。
- 再生成の回数・遅延・利用者への通知、rendererの停止と再開手順。
- 表示先・swapchainなどdevice外の構成が復旧を必要とする場合の統合。
