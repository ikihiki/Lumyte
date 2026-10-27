# Lumyte.Resources.Audio

ResourceManagerはWAVE → `AudioPcmData` → `AudioClip`を構築・共有します。
AudioDecodeStageは既定のResourceRuntime、AudioClipStageはAudio環境を持つAudioResourceRuntimeを使用します。
AudioResourceRuntimeはExecutorとDependenciesを提示し、Managerは環境の選択・世代検証・再構築・解放を管理します。
物理deviceと共有実行器の起動・停止はホストが担当し、deviceの生成・復旧・破棄はAudioModuleが所有します。

- AudioDecodeStage: 既定RuntimeのCPUジョブでSourceの非同期I/Oを待ち、WAVEをデコードしてAudioPcmDataを返す。
- AudioClipStage: 型付きAudioDeviceコマンド一回で、現在のdevice世代のlease取得とclip生成を所有スレッド上で実行し、AudioClipを返す。
- 解放: AudioResourceRuntime.DestroyClipAsyncが生成元backendへのCleanupを完了した後、Managerが登録済みのdevice leaseを返す。

AudioClipは元のIAudioClip参照一本を持つreadonly structです。Managerはclipの生成が完了してから公開します。
AudioClipStageは`IResourceTransform<AudioPcmData, AudioClip, AudioResourceRuntime>`を実装し、BuildAsyncとReleaseAsyncの型付きRuntime引数で環境を受け取ります。
Stageは無引数で作れ、キャストは不要です。環境型の不一致はManager構築時に検出します。

voiceは共有リソースではなく、再生ごとに呼び出し側が生成・制御・解放します。
`AudioClip.CreateVoice(AudioBus.Effects)`は生成完了を待ってAudioVoiceを直接返します。
同じclipから複数の独立したvoiceを作れ、それぞれ再生位置・Gain・Loopを持ちます。
voiceやラッパーのコピーはclipのleaseを取得しません。呼び出し側は**全voiceのDestroy完了まで`ResourceLease<AudioClip>`を保持**します。
早くleaseを返してclipの解放が試みられると、backendが借用中の解放を拒否します。Managerはclipとdevice leaseを保持し、voice解放後のTrimAsyncで再試行できます。

```csharp
using var reference = await resources.RetainAsync(new ResourceKey<AudioClip>("sounds/theme.wav"));
ResourceLease<AudioClip> lease = reference.Pin();
AudioVoice voice = await lease.Value.CreateVoice(AudioBus.Effects);
await voice.Play();
// 再生中もleaseを保持し、終了時にvoiceから解放する。
await voice.Destroy();
lease.Dispose();
```

この例は成功時の順序を示します。voiceの解放が失敗した場合はvoiceとleaseを保持し、解放を再試行してからleaseを返します。
Managerが所有するclipにDestroyClipを直接呼ばないでください。

PCMはclipへのコピー完了までBuild依存として保持します。供給したSamplesはコピー完了まで変更しないでください。
標準のWaveAudioDecoderはRIFF WAVEのmono/stereo PCM8/16/24/32とfloat32を扱います。
入力長が既知なら最大1 MiBを初期容量として確保し、読み取り中の配列拡張を減らします。
これは事前確保の上限で、入力サイズの制限やMemory予算ではありません。全入力とデコード後PCMはメモリへ保持します。
圧縮形式・ストリーミング・巨大ファイルのチャンク分割は独自Stageで追加します。

AudioResourceRuntimeはAudioModuleにDependsOnを宣言し、起動完了後に利用可能になります。
RuntimeはAudioModule.Devicesの世代と可用性をResourcesへ公開し、clipやvoiceを別の辞書で管理しません。
生成結果と正確なdevice leaseを受け取り、世代検証前に候補の解放責任をcontextへ渡します。
検証失敗時はnative候補を解放してからleaseを返し、native解放に失敗した場合は両方を保持してTrimで再試行します。
独自Stage向けのUseAudioAsyncは取得した正確な世代を依存として記録し、deviceのleaseをcontextへ直接渡します。

DIでは`services.AddAudioResources()`でAudioModuleとAudioResourceRuntimeを登録し、共通の登録APIで一つのResourceManagerを構成します。
[DI登録例](../Lumyte.Resources.Audio.DependencyInjection/README.md)を参照してください。

```csharp
services.AddMemoryManagement();
services.AddAudioResources();
services.AddResources(r => r.UseSource<MyAudioSource>().AddAudioPipeline());
```

DIなしではAudioModule、AudioResourceRuntime、汎用のResourceRuntimeを同じExecutionRuntimeへ渡します。
ResourceManagerの既定RuntimeとAudioResourceRuntimeには同じResourceRuntimeを渡してExecutorを共有し、
AudioClipの出力型をstageRuntimesでAudioResourceRuntimeへ接続します。
ManagerはAudioResourceRuntime.Dependenciesからdevice世代の依存先を収集します。

```csharp
var audio = new AudioModule();
var runtime = new ResourceRuntime();
var audioResources = new AudioResourceRuntime(audio, runtime);
var execution = new ExecutionRuntime([audio, audioResources, runtime]);
var resources = new ResourceManager("audio", source,
    [new AudioDecodeStage(), new AudioClipStage()],
    memory, runtime, stageRuntimes:
    [
        new ResourceStageRuntime(typeof(AudioClip), audioResources),
    ]);
```

Host配置ならAudioコマンドはホストへ統合され、CPU処理もCooperativeでPumpします。
Dedicated配置ならAudioの所有スレッドとCPUワーカーを使います。
デコードの既定Runtimeとclip構築のAudioResourceRuntimeは環境が異なるため、別ジョブで進みます。
native操作はAudio ownerへ送り、通常のawait後の継続は同じジョブの配送先へ戻ります。I/O待ちの間はCPU実行枠を返します。
同期的な所有スレッド待機や、フレームごとのAudio更新配送は行いません。

HotReloadは新しいclipを同じStageで構築し、検証後に公開します。
取得済みLeaseは旧clipを保持するため、それを借用中のvoiceは引き続き旧clipを再生します。
新しい音声を使う場合は、新clipのleaseから利用側がvoiceを明示的に生成します。
再生状態・再生位置の移行やクロスフェードは自動で行いません。利用側は旧voiceの`FramePosition`から新voiceへ`Seek`できます。同じsample rateなら`await nextVoice.Seek(Math.Min(oldVoice.FramePosition, nextClip.FrameCount))`で長さに収めます。sample rateが変わる場合は再生時間を介してフレーム位置を換算し、ループや終了位置の方針も選びます。FramePositionは直近の更新結果であり、移行中の再生進行を止めたり、二つのvoiceを同時に切り替えたりする保証はありません。

AudioModuleの更新は所有スレッドで出力状態を確認し、Unavailableになった世代を失効させます。
BrowserのSuspended／interruptedは再開可能なため世代を失効させません。
復旧は同じAudioModuleと固定endpointを使い、アプリが次の順で実行します。

```csharp
await audio.Devices.RecoverAsync(cancellationToken);
await resources.RebuildRuntimeAsync(["Audio"], cancellationToken);
// 旧voiceを解放し、旧clipのLease・Snapshotを返した後に進める。
await resources.TrimAsync(cancellationToken);
await audio.Devices.RetireAsync(cancellationToken);
```

Stageは再構築ごとに新しい世代を取得します。構築中にロストした結果は公開しません。
ロストしたclipはleaseで保持中でも再生には使えず、新deviceへ付け替えません。
ResourceManagerは生成時に選択したRuntimeを各リソース世代へ保持し、解放時に選び直しません。
旧生成物のCleanupは元のbackend instanceから生成元deviceへ送り、旧世代のleaseが残る間はdeviceを破棄しません。
復旧失敗は再試行できます。回復と停止が重なる場合も、factoryの実完了と候補の解放を待ちます。
復旧やスレッド制約の検証は[結合テスト](../../../tests/Audio/Lumyte.Resources.Audio.IntegrationTests/README.md)にあります。

終了時はロード・監視を停止し、voiceの解放完了後に全Reference・Lease・Snapshotを返してResourceManagerを破棄します。
その間もホストはPumpを続け、AudioがCleanupを受け付ける状態を維持します。
その後ExecutionRuntimeを停止し、AudioModuleの世代失効通知、device解放の順に進みます。
