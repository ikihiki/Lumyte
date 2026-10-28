# ADR-AUDIO-0003: Audio操作と世代取得のAPI入口を統一する

- 状態: 採用
- 日付: 2026-10-26
- 置換元: [ADR-AUDIO-0002](AUDIO-0002-execution-and-resources.md)

## 背景

AudioModuleの操作窓口とAudioVoiceの操作で呼び出し方が異なり、モジュール、世代サービス、実際のdeviceの責務を判別しにくい。Graphicsではserviceからleaseを取得し、そのDeviceを操作する。Audioのleaseは所有スレッド限定のbackendを返しており、そのまま呼ぶとスレッド制約にも違反する。

## 決定

Audioも`IAudioDeviceService.AcquireAsync` → `AudioDeviceLease.Device` → device操作の形に揃える。Deviceは取得した正確な物理世代へ配送するAudioDeviceであり、復旧後の現在deviceへ追随しない。AudioModuleは起動・更新・停止と安定したDevices serviceの提供を担当し、State／Commandsの操作窓口を公開しない。内部のライフサイクルcommandと公開deviceの操作を分離する。

AudioDevice、AudioClip、AudioVoiceはいずれも参照一本のreadonly struct senderとする。公開deviceはleaseを参照し、返却後の取得や操作はObjectDisposedException、失効した世代への生成・設定はAudioDeviceUnavailableExceptionで拒否する。実行前にも所有スレッドでleaseと世代を検証する。コピーはleaseを追加取得しない。voice → clipの解放を完了してからleaseを返す。RefreshAsyncは保持した世代だけを更新し、旧世代の失効後は新世代を操作しない。

```csharp
IAudioDeviceService service = audioModule.Devices;
using AudioDeviceLease lease = await service.AcquireAsync();
AudioDevice device = lease.Device;
AudioClip clip = await device.CreateClipAsync(format, pcm);
AudioVoice voice = await clip.CreateVoiceAsync();
await voice.PlayAsync();
await voice.DestroyAsync();
await clip.DestroyClipAsync();
```

AudioResourceRuntimeもAudioModuleではなくIAudioDeviceServiceを受け取り、GraphicsResourceRuntimeと同じくservice経由で世代を取得する。AddAudioResourcesはAudioModuleと同じservice実体をDIへ登録する。Resourcesの独自stageはruntime.AcquireAsyncを使い、選択されたruntimeと同じserviceのleaseをbuild contextへ登録する。別runtimeのcontextは取得前に拒否する。

Resources向けの生成後検証は、deviceのCreateClipWithValidationAsyncでnative生成と同じowner command内で行う。検証例外を候補clipとともに返し、Resourcesがcleanupを登録してから元の例外を再送出する。これにより周期更新が先に障害を観測する競合を防ぎ、解放に失敗した候補と正確な世代をcleanup再試行まで保持する。serviceからの取得と生成は別commandだが、生成は取得済みleaseの世代を検証するため復旧先へ切り替わらない。

RecoverAsyncはowner上で出力状態を確認し、Readyなら生成・通知を増やさず現在のsnapshotを返す。同時復旧は一つのoperationを共有し、取消は呼び出し側の待機だけを止める。復旧開始の内部commandもBeginRecoveryAsyncに一本化する。AudioClipのbus省略はEffectsとし、生成senderの公開名はAsyncのみとする。

### 公開API

比較元はmainの`16d856a`。変更対象の抜粋。生成senderの宣言は代表的なAsync入口を示す。

```diff
 namespace Lumyte.Audio.Execution
 {
     public sealed class AudioModule
     {
-        public AudioDevice State { get; }
         public IAudioDeviceService Devices { get; }
     }
     public sealed class AudioDeviceLease
     {
-        public IAudioDevice Device { get; }
+        // 所有スレッドへ配送する、取得済み世代の窓口。返却・失効後は取得を拒否する。
+        public AudioDevice Device { get; }
     }
     public readonly partial struct AudioDevice
     {
+        // backendではなくAudioDeviceLease.Deviceから取得する。
+        public ValueTask<AudioClip> CreateClipAsync(AudioFormat format, ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default);
+        // 生成後検証の失敗でもclipを返す。呼び出し側はcleanup責任を確保してから例外を扱う。
+        public ValueTask<(AudioClip Clip, Exception? ValidationError)> CreateClipWithValidationAsync(AudioFormat format, ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> SetBusGainAsync(AudioBus bus, float gain, CancellationToken cancellationToken = default);
+        public ValueTask<Unit> RefreshAsync(CancellationToken cancellationToken = default);
     }
+    public static class AudioClipExtensions
+    {
+        // Effectsへ配送する。明示的なbus指定と同じAsync senderを使う。
+        public static ValueTask<AudioVoice> CreateVoiceAsync(this AudioClip clip, CancellationToken cancellationToken = default);
+    }
 }
 namespace Lumyte.Resources.Audio
 {
     public sealed class AudioResourceRuntime
     {
-        public AudioResourceRuntime(AudioModule audio, IResourceRuntime defaultRuntime);
+        public AudioResourceRuntime(IAudioDeviceService devices, IResourceRuntime defaultRuntime);
+        public ValueTask<AudioDeviceLease> AcquireAsync(ResourceBuildContext context, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default);
     }
 }
```

## 検討した代替案

- AudioModule.StateをCommandsへ改名するだけ: 状態との混同は解消するが、Graphicsと異なる取得経路と世代を暗黙選択する操作窓口が残るため採用しない。
- lease.Deviceからbackendを返し続ける: 通常の利用者が所有スレッド限定のAPIを直接呼べてしまうため採用しない。
- 生成と生成後検証を別commandへ分ける: 周期更新が間に入り、検証時の元の例外や候補の解放責任を失う可能性があるため採用しない。
- serviceにモジュールの起動・停止も揃える: GraphicsとAudioでは物理deviceの所有者が異なる。共有する取得と世代管理の契約を揃え、Audioの起動・停止はExecutionModuleに残す。

## 結果と影響

service → lease → device、device → clip → voiceの形で操作対象と寿命を明示できる。Module.State／Commands、deviceのライフサイクルsender、CreateClipWithLease／CreateClipForLeaseの旧入口は残さず、利用側を移行する。Resourcesの生成には世代取得と生成検証の二回のowner配送が必要になる。

検証ではHost／Dedicated、DIあり／なしでdevice解決、キャンセル、旧世代の拒否、返却後の拒否、復旧、clip・voiceの生成・解放と保持世代のretirementを確認する。Resourcesの生成後検証例外、native解放失敗とcleanup再試行も維持する。
