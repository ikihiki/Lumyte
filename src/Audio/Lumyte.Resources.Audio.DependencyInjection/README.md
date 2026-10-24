# Lumyte.Resources.Audio.DependencyInjection

Audioの実装とResourcesの標準パイプラインをDIへ登録します。
Sourceには初期ロードで利用できるIResourceSourceを指定します。ファイル監視やHotReloadは別の機能です。

```csharp
services.AddMemoryManagement();
services.AddAudioResources<MyAudioSource>("audio");
```

AudioModuleとAudioResourceRuntimeをsingletonとして登録します。AddResourcesの既定登録が
汎用ResourceRuntimeとExecutionRuntimeを追加し、三つのmoduleの同じ実体をIExecutionModuleへ接続します。
名前付きResourceManagerの既定RuntimeはResourceRuntimeです。AddAudioPipelineは
`UseStageRuntime<IAudioClip, AudioResourceRuntime>()`と`UseStageRuntime<IAudioVoice, AudioResourceRuntime>()`で
clipとvoiceの出力型へ同じAudioResourceRuntimeを割り当てます。AudioDecodeStageは既定Runtimeを使用します。
AudioResourceRuntimeはその既定RuntimeへCPU配送を委譲し、deviceの取得・所有スレッドへの生成要求・世代依存を管理します。
選択した実体が世代依存先にも自動登録されるため、AddResourceRuntimeDependencyを追加する必要はありません。
複数のmanager名を登録してもmoduleを重複登録しません。
MyAudioSourceとStageはResourceManagerと同じscopeで解決します。
事前に登録したAudioModuleやIAudioDeviceFactoryを使う構成にも対応します。

ホストがExecutionRuntimeを取得し、PrepareOnCurrentHost → StartAsyncを実行します。
起動・ロード・終了中もHost Pumpを維持します。BrowserはHost／Cooperativeを選択します。

```csharp
var execution = provider.GetRequiredService<ExecutionRuntime>();
// Hostを確定するスレッドでPrepareし、起動待ちの間もホストループからPumpする。
execution.PrepareOnCurrentHost(placement, cpuMode, concurrency, new ExecutionOptions());
await execution.StartAsync();

var resources = scope.ServiceProvider.GetRequiredKeyedService<ResourceManager>("audio");
using var reference = await resources.AcquireAsync(new ResourceKey<IAudioVoice>("sounds/theme.wav"));
using var lease = reference.Acquire();
await new AudioVoice(lease.Value).Play();
```

終了時はReference／Leaseを返し、ResourceManagerの破棄を完了させてからExecutionRuntimeを停止します。
デバイス復旧、旧世代の保持、Stageの差し替えは[Audio本体の契約](../Lumyte.Resources.Audio/README.md)を参照してください。

独自のデコードStageを使う場合はAddResourcesでパイプラインを明示的に構成できます。
AddAudioPipelineはWAVE→PCM→Clip→Voiceの標準Stageと、clip／voice用Runtimeの割り当てを登録します。
単独で使用する場合は、同じAudioModuleとAudioResourceRuntimeを実行moduleとして登録済みにします。
