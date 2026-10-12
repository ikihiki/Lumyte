# Lumyte.Audio.Execution

AudioDeviceがデバイス生成・clip生成・bus gain・更新・終了を、AudioClipがvoice生成とclip解放を、AudioVoiceが再生・一時停止・停止・Gain・Loop・状態取得・解放を宣言します。実際のOpenALデバイスとclip・voiceをAudio owner上で生成・制御・解放します。Coreの型付きコマンド生成器を使用します。

`new AudioModule()` またはDIの `AddSingleton<IExecutionModule, AudioModule>()` で登録し、ExecutionRuntimeを起動します。デバイスは起動時に所有スレッド上で生成されます。

```csharp
AudioClip clip = await AudioDevice.CreateClipPort.ExecuteAsync(new AudioFormat(48000, 1), pcm);
AudioVoice original = await AudioClip.Commands.For(clip).CreateVoice(AudioBus.Effects);
var voice = AudioVoice.Commands.For(original);
await voice.Play();
await voice.Stop();
await voice.Destroy();
await AudioClip.Commands.For(clip).DestroyClip();
```

PCMは生成完了まで変更しないでください。返された参照は生成済み実体を保持します。voiceはclipより先に解放します。実行層は生成したclip・voiceの一覧を保持せず、自動解放しません。利用者／Resourcesがvoice→clipの順で解放を完了してからruntimeを停止し、最後にdeviceを解放します。子が残っている場合はbackendがdevice解放を拒否します。

Browserの接続例は[WebAudioサンプル](../../../samples/Lumyte.Audio.Execution.Browser.Sample/README.md)を参照してください。IAudioDeviceFactoryからWebAudioDevice.CreateAsyncをHost上で開始し、OwnerPlacement.HostとCooperativeを使用します。Hostの同期コンテキストとイベントループを維持してください。ResumeOutputAsyncはユーザー操作中に直接開始する別の統合が必要で、サンプルではその間Audio phaseを止め、実際の再開完了を待ちます。

キューは生成された静的portです。同じプロセスで同じモジュールを再登録できません。AudioVoice.State／Gain／Loopはbackendのlockで保護されたcached値を呼び出し元スレッドで直接返し、配送しません。Stateは直近の周期更新のsnapshotです。即時観測が必要ならRefreshPort.ExecuteAsyncで全voiceを一度更新してから読みます。voiceごとの読み取りで全voiceを再更新しません。ECSからは生成されたAddPlayTo等でCommandBatchへ追加できます。

実OpenALのNativeAOT公開は、現行Silk.NET loaderのIL3000／IL3002により未対応です。
