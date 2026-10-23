# ADR-AUDIO-0002: 実行基盤とResourcesによるAudioの生成・制御・復旧

- 状態: 採用
- 日付: 2026-10-23
- 関連カテゴリ: core、resources

## 背景

Audio backendの生成・再生制御・破棄には所有スレッドの制約がある。一方、Sourceの読み込み、デコード、ゲームやECSからの操作は別の実行先で進む。各呼び出し元がラムダやスレッドを管理すると、順序、入力の寿命、停止時の解放責任が分散する。

デバイスロストでは、注入済みのdevice参照を交換するだけでは旧clipと新deviceの混在を防げない。Resourcesの内容世代と物理deviceの世代を接続し、初回ロード、HotReload、復旧で同じStageと所有権を使用する必要がある。Browserでは同じ仕事をホストへ配置し、音声の供給をゲームループからの更新配送に依存させない。

## 決定

### 責務と実行先

`Lumyte.Audio.Execution` はbackendのDevice／Clip／VoiceをCoreの型付きコマンドへ接続する。`AudioModule` がFixedThread ownerと一つのControl order groupを宣言し、生成・制御・解放を受付順に実行する。backendは所属・子資源・借用・破棄状態を管理し、実行層はclip／voiceの一覧やID表を追加しない。

`AudioVoice` と `AudioClip` は元のbackend参照一本を持つreadonly structである。ラッパー自身に宣言したメソッドからsenderを生成し、`new AudioVoice(original).Play()` で静的portへ送信する。ラッパーはnative資源を所有せず、コピーしても寿命を延長しない。呼び出し側はコマンド完了まで元資源を保持する。

Play／Stopの完了はbackend操作の終了を表し、音が聞こえる時刻や再生終了を表さない。取消が実行開始に先行した場合だけ副作用を抑止する。開始後は実際の結果を観測し、返されたValueTaskを一度awaitして結果slotを返す。容量不足は `ExecutionQueueFullException`、受付終了は `InvalidOperationException` として返し、呼び出し側が再試行または要求の集約を決める。await後の継続先にスレッド親和性はない。

State／Gain／Loopのgetterは、backendがlockで保護する保存済みの値を呼び出し元で読む。Stateは直近の更新結果であり、即時観測には `AudioDevice.Refresh()` の完了を待つ。getterからnative更新や全voiceの走査を行わない。

Dedicated配置ではownerが10 ms周期で直接更新し、周期間も制御コマンドを処理する。Host配置では同じ更新とコマンドをControl phaseのPumpで進める。音声サンプルの供給はbackend／デバイスの周期が担当し、この制御ループの周期を再生精度の保証としない。

### Sourceから実体までの所有権

`Lumyte.Resources.Audio` はSource → `AudioPcmData` → `IAudioClip` → `IAudioVoice` を標準Stageとして提供する。ResourceManagerは出力型に対応する `IResourceRuntime` を選び、AudioDecodeStageには既定の `ResourceRuntime`、clipとvoiceには `AudioResourceRuntime` を使用する。AudioResourceRuntimeはStage実行、現在のdevice取得、所有スレッドへの生成要求、世代依存を扱う。Stageの構築と解放のCPU配送は共有の既定Runtimeへ委譲する。

全Stageは無引数で作成できる。AudioDecodeStageはSourceの非同期I/Oを待ってWAVEを同期デコードする。AudioClipStageとAudioVoiceStageは `IResourceTransform<TInput, TOutput, AudioResourceRuntime>` で環境型を宣言し、BuildAsyncの型付きRuntime引数を使う。Stage側の型変換は不要で、不適合な環境登録はManager構築時に拒否する。Stageへ別のRuntimeをconstructor注入する経路は持たない。

通常のawait後の継続もRuntimeがジョブへ戻す。I/Oや依存ロードの待機中はCPU実行枠を占有せず、単一CPU枠やCooperativeでも後続の仕事を進められる。Stageはこの継続先を保持し、ConfigureAwait(false)で汎用Runtimeの配送を外れない。同期デコードの途中では自動分割されないため、大容量デコードは独自Stageでチャンク化する。

標準decoderはRIFF WAVEのmono/stereo PCM8/16/24/32とfloat32を扱う。chunk境界・padding・alignment・sample rate・byte rate・非有限sampleを検証し、不正データは `InvalidDataException`、未対応codecや形式は `NotSupportedException` で失敗する。圧縮形式やストリーミングは別のStageで表す。

PCMはclipへのコピー完了までBuild依存として保持する。voiceはclipをResident依存として保持し、生成完了後に実際のbackend instanceを公開する。`AudioPcmData.Samples` は借用したReadOnlyMemoryであり、利用側が供給した配列もコピー完了まで変更しない。

選択された長寿命の `AudioResourceRuntime` が、構築ごとに `UseAudioAsync` で正確なdevice leaseを取得し、service identityと取得時VersionをResourceManagerへ記録する。同じRuntime実体を世代依存先にも自動登録し、実行環境と依存先を一致させる。構築中に失効した候補は公開せず、voice生成時にはclip.Deviceとlease.Deviceの参照一致も検査する。

ResourceManagerは生成時に選択したRuntimeを各ResourceGenerationへ保持し、同じRuntimeからStageのReleaseAsyncを実行する。解放先を再選択せず、元のbackend instanceへのCleanupコマンドを送る。出力の解放後にdevice leaseを返し、voice → clip → deviceの順を守る。native解放が失敗した場合は同じ出力とleaseを保持して `TrimAsync` で再試行する。旧資源の解放先を新deviceへ付け替えない。

### 安定したserviceと交換可能なepoch

`AudioModule.Devices` はmoduleの存続中に変わらない入口である。Versionは状態遷移ごと、Epochは物理deviceの取得ごとに増える。`InvalidatedThroughVersion` は失ったReady Versionを不可逆に記録する。値の加算はcheckedで行い、周回させない。

初期化はUninitialized → Initializing → Ready、ロストはReady → Lost、復旧はLost／Failed → Recovering → Readyと進む。factory失敗はFailedとなる。Readyはdeviceを使用できる状態であり、Resourcesの再構築完了や実際の音声出力開始を意味しない。

ownerの更新や生成時の確認でUnavailableを観測すると旧epochを失効させる。OpenALは利用可能な場合にALC_EXT_disconnectを確認する。Browserのclosed contextはUnavailableとして扱い、Suspended／interruptedは再開可能な同一世代として保持する。既知の `AudioDeviceUnavailableException` を更新で観測した場合はserviceをLostにし、ExecutionRuntime全体は停止させない。予期しない更新例外は失効後に実行基盤の失敗方針へ渡す。

`AcquireAsync` はownerで現在の使用可能な世代を取得する。使用不能なら `AudioDeviceUnavailableException` で失敗し、暗黙に復旧を待たない。leaseは取得したinstanceを固定し、ロスト後はIsUsableがfalseとなりDeviceへのアクセスを拒否する。Disposeは利用権を一度返すだけで、native破棄を行わない。

`RecoverAsync` の同時要求は生成operationを共有する。呼び出し側の取消はその待機だけを解除する。factoryは所有スレッドで開始され、完了したdeviceの所有権をserviceへ渡す。factory失敗時はfactoryが部分生成物を解放する。完了処理のコマンド受付が容量不足の場合は生成物を保持し、次のRecoverAsyncで完了処理を再試行する。

復旧後はアプリが `RebuildRuntimeAsync(["Audio"])` を実行する。新voiceは停止状態で公開し、再生位置の移行やクロスフェードは利用側が決める。HotReloadも同じStageを使用し、旧Leaseが返るまで旧voice／clipを保持する。ただし物理ロストで失効した値は、保持中でも再生には使えない。

`RetireAsync` はleaseがなくなった旧deviceを元ownerで解放する。backendに子が残る場合や解放が失敗した場合は所有を保持する。OpenALの失ったcontextへ切り替えられない場合、子のnative解放はcontext破棄へ委ね、backendの管理情報を解消する。単なるロスト通知による強制解放は行わない。

### DI、起動と終了

`AddAudioResources<TSource>(name)` はAudioModule・AudioResourceRuntimeをsingletonとして登録する。AddResourcesの既定登録が汎用ResourceRuntimeとExecutionRuntimeを追加し、三つのmoduleの同じ実体をIExecutionModuleへ渡す。AudioResourceRuntimeは共有の既定IResourceRuntimeを受け取る。AddAudioPipelineは `UseStageRuntime<IAudioClip, AudioResourceRuntime>()` とvoice用の同じ指定で出力型を接続し、選択した実体を世代依存先にも自動登録する。PCMは既定Runtimeを使用する。SourceとStageはmanagerのscopeで解決する。複数managerでもmoduleを重複登録しない。メモリ管理は呼び出し側が登録し、事前登録したAudioModuleやIAudioDeviceFactoryでbackendを選べる。無引数のAudioModuleはOpenALを使用する。

DIなしでもAudioModule・AudioResourceRuntime・汎用ResourceRuntimeをExecutionRuntimeへ渡す。ResourceManagerの既定runtimeとAudioResourceRuntimeの委譲先には同じ汎用Runtimeを使用する。`stageRuntimes` へ `ResourceStageRuntime(typeof(IAudioClip), audioRuntime)` とvoice用の指定を渡し、無引数の標準Stageを登録する。世代依存先の重複指定は不要である。ホストが `PrepareOnCurrentHost` → `StartAsync` を実行し、起動完了後にロードを開始する。constructorやDI解決中に非同期生成を同期ブロックしない。

BrowserではHost／Cooperativeを選び、初期化・ロード・終了のawait中もホストの同期コンテキストとPumpを維持する。出力のresumeはuser gesture内で開始するbackend固有の操作として扱う。音声供給callbackからmanaged jobの完了を待たない。

終了時はロード・監視を止め、Reference／Lease／Snapshotを返してResourceManagerの解放を完了させる。その間もAudioのCleanup受付とHost Pumpを維持し、最後にExecutionRuntimeを停止する。AudioModuleはStoppingで新しい取得・復旧を閉じ、進行中factoryと復旧operationの実完了を待つ。未接続の生成物もownerで解放する。子やleaseが残る場合は停止を失敗させて所有を保持し、解放後に停止を再試行する。

## 公開APIと契約

比較元は `main` の `b5702c0`。追加するAudio統合APIの主要メンバーと生成されたsenderを示す。Coreの実行契約は [CORE-0003](../core/CORE-0003-execution-and-jobs.md) に従う。

```diff
+namespace Lumyte.Audio.Abstractions
+{
+    public sealed class AudioDeviceUnavailableException : InvalidOperationException
+    {
+        public AudioDeviceUnavailableException();
+        public AudioDeviceUnavailableException(string message);
+    }
+}
+namespace Lumyte.Audio.Execution
+{
+    public interface IAudioDeviceFactory
+    {
+        // ownerで開始。完了時には実体生成が完了し、所有権をserviceへ渡す。
+        Task<IAudioDevice> CreateAsync();
+    }
+    public sealed class AudioModule : IExecutionModule
+    {
+        public AudioModule(); // OpenAL factoryを使用。
+        public AudioModule(IAudioDeviceFactory factory);
+        public AudioDevice State { get; }
+        public IAudioDeviceService Devices { get; }
+        public PhaseKey HostPhase { get; }
+    }
+    public readonly partial struct AudioVoice
+    {
+        public AudioVoice(IAudioVoice original);
+        // backendの保存済み値。読み取りによるowner配送はない。
+        public AudioPlaybackState State { get; }
+        public float Gain { get; }
+        public bool Loop { get; }
+        // generated。完了はnative操作終了。ValueTaskは一度だけ観測する。
+        public ValueTask<Unit> Play(CancellationToken cancellationToken = default);
+        public ValueTask<Unit> Pause(CancellationToken cancellationToken = default);
+        public ValueTask<Unit> Stop(CancellationToken cancellationToken = default);
+        public ValueTask<Unit> SetGain(float gain, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> SetLoop(bool loop, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> Destroy(CancellationToken cancellationToken = default);
+    }
+    public readonly partial struct AudioClip
+    {
+        public AudioClip(IAudioClip original);
+        public ValueTask<IAudioVoice> CreateVoice(AudioBus bus, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> DestroyClip(CancellationToken cancellationToken = default);
+    }
+    public readonly partial struct AudioDevice
+    {
+        // PCMは完了まで保持。同じserviceの使用可能なleaseを指定する。
+        public ValueTask<IAudioClip> CreateClipForLease(AudioDeviceLease lease, AudioFormat format, ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default);
+        // clipとleaseの生成元deviceが異なる場合はArgumentException。
+        public ValueTask<IAudioVoice> CreateVoiceForLease(AudioDeviceLease lease, IAudioClip clip, AudioBus bus, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> Refresh(CancellationToken cancellationToken = default);
+    }
+    public enum AudioDeviceServiceStatus { Uninitialized, Initializing, Ready, Lost, Recovering, Failed, Stopping, Stopped }
+    public readonly record struct AudioDeviceSnapshot(ulong Version, ulong Epoch, AudioDeviceServiceStatus Status, ulong InvalidatedThroughVersion);
+    public interface IAudioDeviceService
+    {
+        // 保存済み状態。購読者の失敗は他の通知を妨げない。
+        AudioDeviceSnapshot State { get; }
+        event EventHandler? StateChanged;
+        ValueTask<AudioDeviceLease> AcquireAsync(CancellationToken cancellationToken = default);
+        // 共有生成。取消はこの待機のみを解除する。
+        ValueTask<AudioDeviceSnapshot> RecoverAsync(CancellationToken cancellationToken = default);
+        ValueTask<int> RetireAsync(CancellationToken cancellationToken = default);
+    }
+    public sealed class AudioDeviceLease : IDisposable
+    {
+        public AudioDeviceSnapshot State { get; }
+        public bool IsUsable { get; }
+        // ロストはAudioDeviceUnavailableException、返却後はObjectDisposedException。
+        public IAudioDevice Device { get; }
+        public void Dispose(); // native子を先に解放してから利用権を返す。
+    }
+}
+namespace Lumyte.Resources.Audio
+{
+    public sealed class AudioResourceRuntime : IResourceRuntime, IExecutionModule, IResourceRuntimeDependency
+    {
+        public AudioResourceRuntime(AudioModule audio, IResourceRuntime defaultRuntime);
+        // StageのCPU配送は共有のdefaultRuntimeへ委譲する。
+        public ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken);
+        public ValueTask ReleaseAsync(IResourceStage stage, object resource);
+        public string Id { get; } // "Audio"
+        public object Identity { get; } // audio.Devicesと同じ参照。
+        public ResourceRuntimeSnapshot Snapshot { get; }
+        public event EventHandler? Changed;
+        public ValueTask<IAudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default);
+        public ValueTask<IAudioVoice> CreateVoiceAsync(IAudioClip clip, ResourceBuildContext context, CancellationToken cancellationToken = default); // Effects bus。
+    }
+    public static class ResourceAudioBuildExtensions
+    {
+        // leaseの所有権をcontextへ移す。戻り値をStage自身ではDisposeしない。
+        public static ValueTask<AudioDeviceLease> UseAudioAsync(this ResourceBuildContext context, IAudioDeviceService service, string dependencyId = "Audio", ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default);
+    }
+    public sealed class AudioPcmData
+    {
+        public AudioPcmData(AudioFormat format, ReadOnlyMemory<float> samples);
+        public AudioFormat Format { get; }
+        public ReadOnlyMemory<float> Samples { get; }
+    }
+    public static class WaveAudioDecoder
+    {
+        public static AudioPcmData Decode(ReadOnlySpan<byte> bytes);
+    }
+    public sealed class AudioDecodeStage : IResourceSourceStage<AudioPcmData>
+    {
+        public AudioDecodeStage(); // Stageの配送はResourceManagerの既定Runtimeが担当する。
+    }
+    public sealed class AudioClipStage : IResourceTransform<AudioPcmData, IAudioClip, AudioResourceRuntime>
+    {
+        public AudioClipStage(); // 型付きRuntime引数からAudio環境を取得。入力はBuild依存。
+    }
+    public sealed class AudioVoiceStage : IResourceTransform<IAudioClip, IAudioVoice, AudioResourceRuntime>
+    {
+        public AudioVoiceStage(); // 型付きRuntime引数からAudio環境を取得。入力はResident依存。
+    }
+}
+namespace Lumyte.Resources.Audio.DependencyInjection
+{
+    public static class AudioResourceServiceCollectionExtensions
+    {
+        // メモリ管理を登録済みのcollectionへ、名前付きmanagerと共有moduleを登録する。
+        public static IServiceCollection AddAudioResources<TSource>(this IServiceCollection services, string name) where TSource : class, IResourceSource;
+        public static ResourceRegistrationBuilder AddAudioPipeline(this ResourceRegistrationBuilder builder);
+    }
+}
```

## 性能と制約

senderは参照一本で、送信時のクロージャや汎用ExecuteAsyncへの中継を生成しない。静的portの登録は型ごとに一度であり、停止後に同じprocess内で再登録しない。復旧ではportとownerを維持してdeviceだけを交換する。実行基盤のrun・command管理の割り当ては残るため、送信全体のallocation-freeは保証しない。

キュー容量は要求の滞留を制限するが、同期・OSスケジューリング・backend処理の待ち時間は残る。ECSの状態同期は生成されたbatch用メソッドでまとめられる。コマンドごとの遅延上限やhard realtimeは保証しない。

標準デコードはファイル全体をMemoryStreamへ読み、PCM配列も保持する。入力の追加ToArrayコピーは避けるが、デコード中はencodedデータとPCM、clip生成中はPCMとbackendコピーが共存する。これらの配列は共通Memory予算へ自動計上しない。大容量や厳密な予算が必要な用途は、入力上限・チャンク化・メモリ計上を持つStageを使用する。

## 検討した代替案

- 操作ごとにラムダを送る: 呼び出し元のcaptureと寿命管理が増えるため、メソッド宣言から型付きsenderを生成する。
- 実行層にvoice ID表を置く: backendの所有情報を二重管理するため、元instance参照を配送する。
- 常に現在のdeviceへ転送するproxyを置く: 旧clipと新deviceの混在を隠すため、構築ごとに正確なepochを保持する。
- 全更新をゲームループから送る: 送信側の遅延とキュー滞留が更新周期へ加わるため、owner自身が更新する。
- ロストで資源を強制破棄する: 利用中の値と解放責任を失うため、失効と退役を分離する。

## 結果と影響

同じStageとsenderでDedicatedとHostの配置を選べ、Resourcesから実体完成を待って安全に公開できる。device交換をStageの再生成から切り離し、旧世代の保持と解放再試行も表現できる。

利用側には結果の観測、PCMの不変性、停止順、復旧後の再生状態移行の責任が残る。backendごとのスレッド制約とロスト検出も必要であり、静的portの制約から複数の独立AudioModuleを同じprocessで作り直す構成は扱わない。

## 検証方針

単体テストでWAVEの各encoding、不正chunk長・padding・alignment・非有限sample、I/O後のジョブ継続、複数managerのDI登録とmodule共有、出力型ごとのRuntime選択と世代依存先の実体一致を確認する。結合テストはHost／Dedicated、DIあり／なし、fake backend／実OpenALで生成・制御・解放の実行スレッドを確認する。

ロスト注入により旧Leaseの失効、生成中ロスト、旧clipと新deviceの拒否、復旧共有、factory失敗、完了処理の容量不足、復旧中停止、起動前取消、解放失敗の再試行を検証する。HotReloadは新しい停止voiceの公開、旧値の保持、不正入力時の旧公開維持を確認する。物理デバイスの抜き差し、Browserのuser gestureとclosed／interrupted、負荷時の遅延分布はbackend環境で確認する。

## 関連ADR

- [Audio backendの契約](AUDIO-0001-audio-system.md)
- [Core実行基盤](../core/CORE-0003-execution-and-jobs.md)
- [共通Resources Runtime](../resources/RESOURCES-0003-runtime-execution.md)
- [Audioの利用と復旧](../../../src/Audio/Lumyte.Resources.Audio/README.md)
- [DI登録](../../../src/Audio/Lumyte.Resources.Audio.DependencyInjection/README.md)
