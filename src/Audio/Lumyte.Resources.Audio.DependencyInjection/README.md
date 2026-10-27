# Lumyte.Resources.Audio.DependencyInjection

Audioの実行moduleと、ResourcesのWAVE → PCM → ClipパイプラインをDIへ登録します。
Sourceには初期ロードで利用できるIResourceSourceを指定します。ファイル監視やHotReloadは別の機能です。

```csharp
services.AddMemoryManagement();
services.AddAudioResources();
services.AddResources(r => r.UseSource<MyAudioSource>().AddAudioPipeline());
```

無引数のAddAudioResourcesはAudioModuleとAudioResourceRuntimeをsingleton登録し、AudioModule.Devicesと同じIAudioDeviceServiceを解決できるようにします。
共通のAddResourcesが汎用ResourceRuntime、ExecutionRuntimeと、一つの名前なしsingleton ResourceManagerを登録します。
同じmodule実体をIExecutionModuleへ接続し、SourceとStageもsingleton Managerの寿命で解決します。scopeや名前でManagerを選び分けません。

AddAudioPipelineはAudioDecodeStageとAudioClipStageを登録し、
`UseStageRuntime<AudioClip, AudioResourceRuntime>()`でclipの出力型へAudio環境を割り当てます。
AudioDecodeStageは既定Runtimeを使用します。
AudioResourceRuntimeはそのExecutorを共有し、deviceの取得・所有スレッドへの生成と解放・世代依存を提供します。
ManagerがDependenciesから同じ世代providerを収集するため、依存先を別途登録する必要はありません。
事前に登録したAudioModuleやIAudioDeviceFactoryを使う構成にも対応します。

ホストがExecutionRuntimeを取得し、PrepareOnCurrentHost → StartAsyncを実行します。
起動・ロード・終了中もHost Pumpを維持します。BrowserはHost／Cooperativeを選択します。

```csharp
var execution = provider.GetRequiredService<ExecutionRuntime>();
// Hostを確定するスレッドでPrepareし、起動待ちの間もホストループからPumpする。
execution.PrepareOnCurrentHost(placement, cpuMode, concurrency, new ExecutionOptions());
await execution.StartAsync();

var resources = provider.GetRequiredService<ResourceManager>();
using var reference = await resources.RetainAsync(new ResourceKey<AudioClip>("sounds/theme.wav"));
ResourceLease<AudioClip> lease = reference.Pin();
AudioVoice voice = await lease.Value.CreateVoice(AudioBus.Effects);
await voice.Play();
// 再生中もleaseを保持する。終了時はvoiceの解放完了を先に待つ。
await voice.Destroy();
lease.Dispose();
```

voiceは再生ごとに利用側が作成し、Managerには登録しません。同じclipから複数のvoiceを作れ、それぞれ`await voice.Seek(framePosition)`で独立した位置へ移動できます。`FramePosition`は直近のAudio更新時の位置を配送なしで返します。
ラッパーのコピーやvoice生成はleaseを取得しないため、全voiceのDestroyが完了するまでclipのleaseを保持します。
上の例は成功時の順序です。解放失敗時はvoiceとleaseを保持し、再試行後に返却してください。

終了時は全voiceの解放後にReference／Leaseを返し、ResourceManagerの破棄を完了させてからExecutionRuntimeを停止します。
デバイス復旧、旧世代の保持、Stageの差し替えは[Audio本体の契約](../Lumyte.Resources.Audio/README.md)を参照してください。
独自のデコードStageを使う場合はAddResourcesでパイプラインを明示的に構成できます。
