# Lumyte.Audio.Execution

AudioDeviceがデバイス生成・clip生成・bus gain・更新・終了を、AudioClipがvoice生成とclip解放を、AudioVoiceが再生・一時停止・停止・Seek・Gain・Loop・状態と位置の取得・解放を宣言します。実際のOpenALデバイスとclip・voiceをAudio owner上で生成・制御・解放します。Coreの型付きコマンド生成器を使用します。

`new AudioModule()` またはDIの `AddSingleton<IExecutionModule, AudioModule>()` で登録し、ExecutionRuntimeを起動します。デバイスは起動時に所有スレッド上で生成されます。

```csharp
var clip = new AudioClip(await audio.State.CreateClip(new AudioFormat(48000, 1), pcm));
AudioVoice voice = await clip.CreateVoice(AudioBus.Effects);
await voice.Seek(clip.FrameCount / 2); // PCMフレーム単位。停止中なので再生は始まらない。
await voice.Play();
await voice.Stop();
await voice.Destroy();
await clip.DestroyClip();
```

PCMは生成完了まで変更しないでください。返された参照は生成済み実体を保持します。voiceはclipより先に解放します。実行層は生成したclip・voiceの一覧を保持せず、自動解放しません。利用者／Resourcesがvoice→clipの順で解放を完了してからruntimeを停止し、最後にdeviceを解放します。子が残っている場合はbackendがdevice解放を拒否します。

Browserの接続例は[WebAudioサンプル](../../../samples/Lumyte.Audio.Execution.Browser.Sample/README.md)を参照してください。IAudioDeviceFactoryからWebAudioDevice.CreateAsyncをHost上で開始し、OwnerPlacement.HostとCooperativeを使用します。Hostの同期コンテキストとイベントループを維持してください。ResumeOutputAsyncはユーザー操作中に直接開始する別の統合が必要で、サンプルではその間Audio phaseを止め、実際の再開完了を待ちます。

キューは生成された静的portです。同じプロセスで同じモジュールを再登録できません。AudioVoice.State／FramePosition／Gain／Loopはbackendのlockで保護されたcached値を呼び出し元スレッドで直接返し、配送しません。StateとFramePositionは直近の周期更新のsnapshotです。即時観測が必要ならaudio.State.Refreshで全voiceを一度更新してから読みます。voiceごとの読み取りで全voiceを再更新しません。ECSからは生成されたAddPlayTo／AddSeekTo等でCommandBatchへ追加できます。

`Seek(framePosition)`は`0..clip.FrameCount`を受け付け、所有スレッドで実際に位置を変更してから完了します。再生中のSeekは再生を続け、一時停止・停止中はそのモードを保ちます。範囲外は状態を変えずに拒否します。停止は位置を0へ戻し、自然終了はFrameCountです。ループ中の末尾指定は0へ戻ります。詳細な端点の遷移は[backend契約](../../../docs/adr/audio/AUDIO-0001-audio-system.md#seekと位置の観測)を参照してください。Seekの完了は可聴時刻やスナップショット更新を保証しません。直後の値が必要なら`await audio.State.Refresh()`を待って読みます。

NativeAOTはLinux x64でDIあり／なし、Host／Dedicatedを検証しています。OpenALはBCLのNativeLibraryで読み込み、Silk.NETの独自path resolverを使用しません。nativeライブラリの配置は[OpenAL backend](../Lumyte.Audio.OpenAL/README.md)を参照してください。

AudioVoiceとAudioClipは参照一本のreadonly structです。元のIAudioVoice／IAudioClipから直接構築でき、AudioClip.CreateVoiceは生成済みのAudioVoiceを返します。同じclipから独立したvoiceを複数作れます。元clipのDeviceは生成元のbackend deviceへの不変参照で、voice生成もそのbackendへ配送します。ラッパーのコピーやvoice生成はResourcesのleaseを取得しません。Resourcesから借りたclipは、全voiceのDestroyが完了するまで`ResourceLease<AudioClip>`を保持してください。

## デバイス世代と復旧

`audio.Devices` は同じAudioModule内で存続する `IAudioDeviceService` です。`State` と `StateChanged` は現在のVersion、物理Epoch、利用可否と失効済みVersionを公開します。所有スレッドの周期更新が出力状態を確認し、Unavailableになった世代を不可逆に失効させます。BrowserのSuspended／interruptedは同じ利用可能な世代として扱い、復旧や資源再生成を要求しません。

```csharp
(AudioDeviceLease lease, IAudioClip original, Exception? validationError) =
    await audio.State.CreateClipWithLease(format, pcm);
try
{
    if (validationError is not null)
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(validationError);
    if (!lease.IsUsable)
        throw new AudioDeviceUnavailableException();
    // originalを利用する間はleaseを保持する。
}
finally
{
    await new AudioClip(original).DestroyClip();
    lease.Dispose(); // native解放に失敗した場合は返さず、両方を保持して再試行する。
}

// Lost／Failed時。新しいdeviceも同じ所有スレッドで生成する。
AudioDeviceSnapshot ready = await audio.Devices.RecoverAsync();
// Resourcesが新世代のclipを再構築し、利用側が必要なvoiceを作成する。
int retired = await audio.Devices.RetireAsync();
```

CreateClipWithLeaseは一回のowner commandで世代取得・native生成・生成後の使用可否確認を行います。生成失敗時は取得したleaseを返します。生成後の確認が例外を投げた場合は候補とleaseを失わないようValidationErrorへ保持して返すため、低水準APIの利用側は必ずこの値とIsUsableを確認し、候補を解放してからleaseを返します。通常のAudioResourceRuntime.CreateClipAsyncは解放責任を登録してから元の例外を再送出し、呼び出し側へAudioClipまたは例外だけを返します。
leaseは取得時の正確な世代を保持し、新しいdeviceへ追随しません。AudioClip.CreateVoiceはclipの生成元deviceへ配送し、新しいdeviceへ付け替えません。失効したclip・voiceの操作は失敗しますが、Cleanupは元のdeviceで引き続き実行できます。旧子資源の解放とlease返却後にRetireAsyncを呼びます。leaseなしで直接生成した子資源もbackendが保持している間はdevice破棄を拒否します。serviceが世代を失効させると、backendのInvalidateがnative呼び出しなしで利用不能を記録し、状態確認の例外後も元clip／voiceからの操作を拒否します。サービスはclip／voiceの一覧を追加しません。

RecoverAsyncは同時要求で一つの生成operationを共有します。呼び出し側の取消は待機だけを取り消します。生成完了のコマンド受付が容量不足で失敗した場合、次のRecoverAsyncが保持済みoperationの完了処理を再試行します。factory失敗やdevice解放失敗の後も再試行できます。終了は新しい取得と復旧を閉じ、進行中factoryの実完了を待ち、未接続の生成物も所有スレッドで解放します。未返却leaseや解放できない子資源がある場合は停止を失敗させ、所有権を保持します。

Resourcesへの世代失効・再構築の接続は `Lumyte.Resources.Audio` が担当します。音声の再生位置や再生中状態の復元は利用側の方針です。静的portの再登録やExecutionRuntime全体の再作成は行いません。
