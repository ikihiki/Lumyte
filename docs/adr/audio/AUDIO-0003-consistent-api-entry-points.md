# ADR-AUDIO-0003: Audio操作と世代取得のAPI入口を統一する

- 状態: 採用
- 日付: 2026-10-26
- 置換元: [ADR-AUDIO-0002](AUDIO-0002-execution-and-resources.md)

## 背景

AudioModule.Stateは操作用senderを返す一方、Devices.Stateは状態のsnapshotを返す。同じStateという名前から役割を予測しにくい。GraphicsのResourceRuntimeは選択済みruntimeから世代を取得できるが、Audioの独自stageはdevice serviceを別途受け取る必要がある。復旧の要求がReadyへ遅れて到着した場合も、Audioだけが例外になる。

## 決定

操作はCommands、状態はDevices.Stateから取得する。旧Stateプロパティは削除し、Commandsに統一する。AudioClipのbus省略はbackendと同じEffectsにする。

RecoverAsyncはowner上で出力状態を確認し、Readyなら新規生成や状態変更をせず、そのsnapshotを返す。同時要求は従来どおり一つのoperationを共有する。Lost／Failedなら復旧し、生成途中の取消は呼び出し側の待機だけを取り消す。取消済み要求はReadyでも取消を返す。ownerでの確認後に起きたロストまでsnapshotの利用可能性を保証しない。

独自stageはAudioResourceRuntime.AcquireAsyncを使い、選択されたruntimeと同じdevice serviceのleaseをbuild contextへ登録する。別runtimeのcontextは取得前に拒否する。leaseを返すのはManagerであり、native操作の所有スレッド制約は変わらない。

このADRは置換元のAPI入口とReady時の復旧契約を変更する。配送、clip／voiceの所有権、起動・停止、世代失効とcleanupの判断は置換元を引き継ぐ。

復旧開始のcommandもBeginRecoveryAsyncへ一本化し、Readyなら現在snapshot、それ以外は復旧を開始してnullを返す。旧void版と互換用のBeginRecoveryIfNeededは残さない。

### 公開API

比較元はmainの`16d856a`。以下は変更対象の抜粋。

```diff
 namespace Lumyte.Audio.Execution
 {
     public sealed class AudioModule
     {
+        // 操作用sender。Devices.Stateのsnapshotとは区別する。
+        public AudioDevice Commands { get; }
-        public AudioDevice State { get; }
     }
+    public static class AudioClipExtensions
+    {
+        // Effectsへ配送する拡張メソッド。明示的なbusを受け取るinstance methodへ配送する。
+        // Async命名からもbusを省略できる。同じAsync senderへ配送する。
+        public static ValueTask<AudioVoice> CreateVoiceAsync(this AudioClip clip, CancellationToken cancellationToken = default);
+    }
     public interface IAudioDeviceService
     {
         // Readyではownerが確認したsnapshotを返す。Lost／Failedでは共有operationで復旧する。
         // cancellationTokenはこの呼び出しの待機を取り消し、開始済み生成は継続する。
         ValueTask<AudioDeviceSnapshot> RecoverAsync(CancellationToken cancellationToken = default);
     }
 }
 namespace Lumyte.Resources.Audio
 {
     public sealed class AudioResourceRuntime
     {
+        // 選択済みruntimeから取得したleaseをcontextへ渡す。違うcontext.RuntimeはInvalidOperationException。
+        // lifetimeの既定はResident。native解放後にManagerがleaseを返す。
+        public ValueTask<AudioDeviceLease> AcquireAsync(ResourceBuildContext context, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default);
     }
 }
```

## 検討した代替案

- Stateを互換入口として残す: 同じsenderに複数の名前が生じるため採用しない。利用側をCommandsへ移行する。
- Ready判定を呼び出し側で行う: 判定とownerでの実行の間に状態が変わるため、service側で判定する。
- 全device serviceに同じ初期化・破棄APIを追加する: GraphicsのscopeとAudioのExecutionModuleでは所有者が異なる。共通の役割だけを揃える。

## 結果と影響

既存の明示的bus指定、State利用、ResourcesのUseAudioAsyncは引き続き動作する。新規コードはCommandsとruntime.AcquireAsyncを選べる。生成器の命名規則は変更せず、この変更だけで利用できる。

検証ではReady時の再要求、同時復旧、取消後も共有復旧が完了すること、取得した世代の失効とManagerによるlease返却、bus省略時のEffects配送を確認する。HostとDedicatedの両方で実行する。
