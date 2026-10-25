# ADR-AUDIO-0002: 実行基盤とResourcesによるAudioの生成・制御・復旧

- 状態: 採用
- 日付: 2026-10-23
- 関連カテゴリ: core、resources

## 背景

Audio backendの生成・再生制御・破棄には所有スレッドの制約がある。一方、Sourceの読み込み、デコード、ゲームやECSからの操作は別の実行先で進む。呼び出し元ごとにラムダとスレッドを管理せず、同じ型付きコマンドを専用スレッドとBrowserのホスト上で実行したい。

Resourcesが共有する音声データと、再生位置や状態を持つvoiceは寿命が異なる。デバイスロストやHotReloadでもclipとvoiceの所有権を保ち、旧clipを新deviceへ渡さずに再構築・解放する必要がある。

## 決定

### 所有スレッドへの配送

`Lumyte.Audio.Execution`はDevice／Clip／VoiceをCoreの型付きコマンドへ接続する。`AudioModule`がFixedThread ownerと一つのControl order groupを宣言し、生成・制御・解放を受付順に実行する。backendが所属・子資源・借用・破棄状態を管理し、実行層にclip／voiceの一覧やID表を追加しない。

`AudioClip`と`AudioVoice`は元のbackend参照一本を持つreadonly structである。自身に宣言したメソッドからsenderを生成し、静的portへ送信する。`AudioClip.CreateVoice(bus)`は生成済みの`AudioVoice`を直接返す。ラッパーはnative資源やResourcesのleaseを所有せず、コピーしても寿命を延長しない。

Play／Stopの完了はbackend操作の終了を表し、可聴時刻や再生終了は保証しない。取消が実行開始に先行した場合だけ副作用を抑止する。開始後は実際の結果を観測し、ValueTaskを一度awaitして結果slotを返す。容量不足は`ExecutionQueueFullException`、受付終了は`InvalidOperationException`で返す。await後の呼び出し側に所有スレッドへの親和性を要求しない。

State／Gain／Loopはbackendがlockで保護する保存済みの値を呼び出し元で読む。Stateは直近の更新結果で、即時観測には`AudioDevice.Refresh()`を待つ。getterからnative更新や全voiceの走査は行わない。

Dedicatedではownerが10 ms周期で直接更新し、周期間も制御コマンドを処理する。一回の処理区間を64件・2 msまでとし、区間ごとに更新期限を確認して残りの要求を継続する。次の10 ms周期を待つ件数制限ではない。HostではControl phaseのPumpで同じ仕事を進める。音声サンプルの供給はbackend／デバイスの周期が担当し、この制御ループを再生精度の保証としない。

### Resourcesはclipを共有し、利用側がvoiceを所有する

標準パイプラインはSource → `AudioPcmData` → `AudioClip`で完了する。AudioDecodeStageは既定のResourceRuntimeで非同期I/OとWAVEデコードを行い、AudioClipStageはAudioResourceRuntimeでclipを生成する。voice用Stageや共有voiceのキャッシュは設けない。同じclipから呼び出し側が独立したvoiceを必要な数だけ生成する。

全Stageは無引数で作成できる。AudioClipStageは`IResourceTransform<AudioPcmData, AudioClip, AudioResourceRuntime>`を実装し、BuildAsyncとReleaseAsyncの型付き引数で環境を受け取る。Stageに別のRuntimeをconstructor注入せず、環境型の不一致はManager構築時に検出する。

AudioResourceRuntimeは安定した実行環境として、既定Runtimeと共有するExecutor、deviceの取得・所有スレッドへの生成と解放、device世代を表すDependenciesを提示する。Managerが環境の選択・利用期間・世代検証・失効・再構築を管理し、ホストとAudioModuleが物理deviceと共有実行器の起動・停止を担う。

デコードとclip構築は環境が異なるため別ジョブで進める。同一Runtime内の連続Stageは共通Runtimeの規則に従って同じジョブを使う。通常のawait後も実行器が継続をジョブへ戻し、I/Oや依存ロードの待機中はCPU枠を返す。native操作はAudio ownerへ配送する。同期デコードは自動分割しない。

PCMはコピー完了までBuild依存として保持し、その間はSamplesを変更しない。標準decoderはRIFF WAVEのmono/stereo PCM8/16/24/32とfloat32を扱い、不正chunk・alignment・sample rate・非有限sampleを拒否する。圧縮形式やストリーミングは独自Stageで扱う。

AudioResourceRuntime.CreateClipAsyncは一回のowner commandで実体と正確なdevice leaseを取得する。生成失敗時はowner上でleaseを返す。生成直後も同じowner command内で使用可否を確認し、確認例外は候補とleaseに添えて返す。Runtimeは例外の再送出と世代検証の前に候補とleaseの解放責任をcontextへ登録し、取得時のservice identityとVersionを記録する。失効した候補は公開しない。候補のnative解放が失敗した場合も両方を保持し、Trimで再試行する。独自StageのUseAudioAsyncも取得した正確なdevice leaseをcontextへ直接渡す。

Managerは生成時のRuntimeを各リソース世代に保持し、ReleaseAsyncへ渡す。DestroyClipAsyncは元backendへのCleanupが完了してからdevice leaseを返し、解放失敗時は両方を保持する。解放先を新deviceへ付け替えない。

利用側は**clipから作った全voiceのDestroyが完了するまで`ResourceLease<AudioClip>`を保持する**。voiceに暗黙のleaseや追加の管理オブジェクトは持たせない。先にleaseを返してclip解放が試みられると、backendは借用中として拒否し、Managerはvoice解放後のTrimで再試行する。Manager所有のclipを利用側から直接DestroyClipしない。

### device世代、復旧とHotReload

`AudioModule.Devices`はmoduleの存続中に変わらない入口である。Versionは状態遷移ごと、Epochは物理deviceの取得ごとに増える。InvalidatedThroughVersionは失ったReady Versionを不可逆に記録する。加算はcheckedとし、周回させない。

初期化はUninitialized → Initializing → Ready、ロストと復旧はReady → Lost → Recovering → Readyと進み、factory失敗はFailedとなる。Readyはdeviceの使用可否であり、Resourcesの再構築完了や可聴出力開始を表さない。

ownerの更新や生成時の確認でUnavailableを観測すると旧epochを失効させる。OpenALは利用可能な場合にALC_EXT_disconnectを確認する。Browserのclosed contextはUnavailable、Suspended／interruptedは再開可能な同一世代として扱う。既知のAudioDeviceUnavailableExceptionはserviceをLostにし、ExecutionRuntime全体は止めない。serviceが失効を決めるとbackendのInvalidateで利用不能を不可逆に記録し、予期しない確認例外の後も取得済みclip／voiceの操作を拒否する。Invalidateはnativeを呼ばずCleanupを維持する。予期しない更新例外は失効後に実行基盤の失敗方針へ渡す。

AcquireAsyncはownerで使用可能な世代を取得し、使用不能ならAudioDeviceUnavailableExceptionで失敗する。leaseは取得したinstanceに固定され、ロスト後はIsUsableがfalseとなりDeviceへのアクセスを拒否する。Disposeは利用権だけを返し、native破棄は行わない。

RecoverAsyncの同時要求は生成operationを共有し、呼び出し側の取消は待機だけを解除する。factoryはownerで開始され、完成したdeviceの所有権をserviceへ渡す。factoryは失敗時の部分生成物を解放する。完了commandの容量不足では生成物を保持し、次のRecoverAsyncで完了処理を再試行する。

復旧後はアプリがRebuildRuntimeAsync(["Audio"])を実行し、新deviceのclipを公開する。HotReloadも同じStageで新clipを構築する。取得済みleaseは旧clipを保持し、それを借用するvoiceは旧clipを使い続ける。利用側は新clipからvoiceを明示的に作成し、再生位置の移行やクロスフェードを決める。ただし物理ロストで失効したclipは保持中でも再生には使えない。

RetireAsyncはleaseがなくなった旧deviceを元ownerで解放する。子が残る場合や解放失敗時は所有を保持する。OpenALの失ったcontextへ切り替えられない場合は、子のnative解放をcontext破棄へ委ね、backend管理情報を解消する。ロスト通知だけで利用中資源を強制破棄しない。

### DI、起動と終了

DIは無引数の`AddAudioResources()`でAudioModuleとAudioResourceRuntimeだけをsingleton登録し、`AddResources(r => r.UseSource<TSource>().AddAudioPipeline())`で一つの名前なしsingleton Managerを構成する。SourceとStageもその寿命で解決し、scopeやkeyでは選ばない。AddAudioPipelineはDecode／Clip Stageと`UseStageRuntime<AudioClip, AudioResourceRuntime>()`を登録する。PCMは既定Runtimeを使い、世代依存先は環境のDependenciesから収集する。メモリ管理は呼び出し側が登録する。

共通AddResourcesがResourceRuntimeとExecutionRuntimeを登録し、三つのmoduleの同じ実体をIExecutionModuleへ接続する。AudioResourceRuntimeはAudioModuleへの依存を宣言するが、deviceを起動・停止しない。事前登録したAudioModuleやIAudioDeviceFactoryでbackendを選べ、無引数のAudioModuleはOpenALを使う。

DIなしでも同じ三つのmoduleをExecutionRuntimeへ渡す。Managerの既定RuntimeとAudioResourceRuntimeに同じ汎用Runtimeを指定し、stageRuntimesへ`ResourceStageRuntime(typeof(AudioClip), audioRuntime)`を渡す。ホストがPrepareOnCurrentHost → StartAsyncを進め、起動完了後にロードする。具体例は[Resources README](../../../src/Audio/Lumyte.Resources.Audio/README.md)と[DI README](../../../src/Audio/Lumyte.Resources.Audio.DependencyInjection/README.md)に記載する。

BrowserはHost／Cooperativeを選び、起動・ロード・終了のawait中も同期コンテキストとPumpを維持する。出力resumeはuser gesture内で始めるbackend固有操作であり、音声供給callbackからmanaged jobを待たない。

終了はロード・監視の停止 → 全voiceの解放 → Reference／Lease／Snapshotの返却 → Managerの解放 → ExecutionRuntimeの停止とする。AudioのCleanup受付とHost PumpはManagerの解放完了まで維持する。AudioModuleはStoppingで新規取得・復旧を閉じ、進行中factoryの実完了と未接続生成物の解放を待つ。子やleaseが残れば所有を保持して停止を失敗させ、解放後に再試行する。

## 主な公開API

senderのメソッドは各ラッパーの宣言から生成する。省略した制御メソッドやdevice serviceの詳細は実装と[Audio実行層README](../../../src/Audio/Lumyte.Audio.Execution/README.md)を参照する。

```csharp
public readonly partial struct AudioClip
{
    public AudioClip(IAudioClip original);
    public ValueTask<AudioVoice> CreateVoice(AudioBus bus, CancellationToken cancellationToken = default);
    public ValueTask<Unit> DestroyClip(CancellationToken cancellationToken = default);
}

public readonly partial struct AudioVoice
{
    public AudioVoice(IAudioVoice original);
    public ValueTask<Unit> Play(CancellationToken cancellationToken = default);
    public ValueTask<Unit> Stop(CancellationToken cancellationToken = default);
    public ValueTask<Unit> Destroy(CancellationToken cancellationToken = default);
}

public sealed class AudioResourceRuntime : IResourceRuntime, IExecutionModule, IResourceRuntimeDependency
{
    public AudioResourceRuntime(AudioModule audio, IResourceRuntime defaultRuntime);
    public ValueTask<AudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default);
    public ValueTask DestroyClipAsync(AudioClip clip);
}
```

## 性能と制約

senderは参照一本で、送信時のクロージャや汎用ExecuteAsyncへの中継を生成しない。静的portの登録は型ごとに一度で、復旧ではportとownerを維持してdeviceだけを交換する。run・command管理の割り当ては残るため、送信全体のallocation-freeは保証しない。ECSの状態同期は生成されたbatch用メソッドでまとめられるが、同期・OSスケジューリング・backend処理があるためhard realtimeは保証しない。

DIのconstructor登録に使う型引数はPublicConstructorsを保持する。OpenALはBCLのNativeLibrary経由で標準のapp／system探索を使い、single-fileで使えないAssembly.LocationやCodeBaseに依存しない。Linux x64で警告を抑制しないNativeAOT公開とDIあり／なしの実行を検証する。

OpenALの一時PCM16変換領域はArrayPoolから借り、実サンプル数だけをBufferDataへ渡して必ず返す。長寿命のdecoded PCMはプールへ返さず、短期の変換領域と寿命を分ける。標準デコードは入力のToArrayコピーを避けるが、encodedデータとPCM、生成中のPCMとbackendコピーは共存する。これらの配列は共通Memory予算へ自動計上しない。大容量用途は入力上限・チャンク化・メモリ計上を持つStageを使う。

## 検討した代替案

- 操作ごとにラムダを送る: captureと寿命管理が増えるため、メソッドから型付きsenderを生成する。
- 実行層にvoice ID表を置く: backendの所有情報を二重管理するため、元instance参照を配送する。
- voiceを共有リソースとしてキャッシュする: 再生位置や状態まで共有してしまうため、Managerはclipを共有し、利用側がvoiceを所有する。
- voiceごとに暗黙のclip leaseを追加する: ラッパーの保持量と所有権を増やすため、利用側が明示的にclip leaseを保持する。
- 現在のdeviceへ転送するproxyやロスト時の強制破棄: 旧clipとの混在や利用中資源の所有権を隠すため、正確なepochを保持して失効と退役を分離する。

## 結果と検証

同じsenderとStageをDedicated／Hostで使い、実体完成を待って公開できる。共有clipと個別voiceの寿命を分離できる一方、利用側には全voiceの解放完了までleaseを保持する責任と、復旧後の再生方針が残る。静的portの制約から独立したAudioModuleを同じprocessで作り直す構成は扱わない。

単体テストはWAVEの各形式と不正入力、I/O後のジョブ継続、一つのsingleton ManagerのDI登録、型付きRuntimeと世代依存先の一致を確認する。結合テストはHost／Dedicated、DIあり／なし、fake／OpenALで生成・制御・解放のスレッド、複数voiceの独立性、借用中clipの解放拒否と再試行を確認する。

ロスト・復旧・HotReloadは旧lease保持、生成中失効、旧clipと新deviceの拒否、復旧共有、factory失敗、容量不足、停止との競合、起動取消、解放失敗の再試行、不正入力時の旧公開維持を検証する。物理抜き差し、Browserのuser gestureとclosed／interrupted、負荷時の遅延分布は実環境で確認する。

## 関連ADR

- [Audio backendの契約](AUDIO-0001-audio-system.md)
- [Core実行基盤](../core/CORE-0003-execution-and-jobs.md)
- [共通Resources Runtime](../resources/RESOURCES-0003-runtime-execution.md)
