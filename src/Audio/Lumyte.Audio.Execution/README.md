# Lumyte.Audio.Execution

実際のOpenALデバイスとclip・voiceをAudio owner上で生成・制御・解放します。Coreの型付きコマンド生成器を使用します。

`new AudioModule()` またはDIの `AddSingleton<IExecutionModule, AudioModule>()` で登録し、ExecutionRuntimeを起動します。デバイスは起動時に所有スレッド上で生成されます。

```csharp
AudioClip clip = await AudioCommands.CreateClipPort.ExecuteAsync(new AudioFormat(48000, 1), pcm);
AudioVoice original = await AudioCommands.CreateVoicePort.ExecuteAsync(clip, AudioBus.Effects);
var voice = AudioCommands.Voice.For(original);
await voice.Play();
await voice.Stop();
await voice.Destroy();
await AudioCommands.Clip.For(clip).DestroyClip();
```

PCMは生成完了まで変更しないでください。返された参照は生成済み実体を保持します。voiceはclipより先に解放します。終了時は未解放voice、clip、deviceの順に所有スレッド上で解放します。失敗した解放は再試行可能です。

Browserの接続例は[WebAudioサンプル](../../../samples/Lumyte.Audio.Execution.Browser.Sample/README.md)を参照してください。IAudioDeviceFactoryからWebAudioDevice.CreateAsyncをHost上で開始し、OwnerPlacement.HostとCooperativeを使用します。Hostの同期コンテキストとイベントループを維持してください。ResumeOutputAsyncはユーザー操作中に直接開始する別の統合が必要で、サンプルではその間Audio phaseを止め、実際の再開完了を待ちます。

キューは生成された静的portです。同じプロセスで同じモジュールを再登録できません。ReadStateは直近の周期更新のsnapshotを返します。即時観測が必要ならRefreshPort.ExecuteAsyncで全voiceを一度更新してから読みます。voiceごとの読み取りで全voiceを再更新しません。ECSからは生成されたAddPlayTo等でCommandBatchへ追加できます。

実OpenALのNativeAOT公開は、現行Silk.NET loaderのIL3000／IL3002により未対応です。
