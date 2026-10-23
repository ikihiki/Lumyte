# Lumyte.Resources.Audio

ResourcesのRuntimeを既存のAudioModuleと接続します。
初回ロードとHotReloadのどちらも同じStageを使用します。

- アプリのデコードStage: 非同期I/O → ResourceJobによるCPU処理 → AudioPcmData。
- AudioClipStage: AudioResourceRuntimeで現在のdevice世代を取得 → 型付きAudioDeviceコマンド → 所有スレッドでclip生成。
- AudioVoiceStage: 同じ手順で世代を取得し、clipの生成元との一致を確認してvoice生成。
- 解放: voice → clipの順で、生成済みのCleanupコマンドを所有スレッドへ配送。

生成完了まではawaitし、公開する値は実際のIAudioClip・IAudioVoiceです。
voiceはclipをResident依存として保持し、PCMはclipへのコピー完了までBuild依存として保持します。
デコード形式・メモリ予算・圧縮ストリームの方針はアプリのStageが定めます。
AudioResourceRuntimeはAudioModuleにDependsOnを宣言し、起動完了後に利用可能になります。
RuntimeはAudioModule.Devicesの世代と可用性をResourcesへ公開し、clipやvoiceを別の辞書で管理しません。
UseAudioAsyncは取得した正確な世代を依存として記録し、deviceのleaseをcontextへ渡します。
StageのReleaseAsyncが子資源を解放した後にleaseを返し、解放失敗時は同じ世代を保持して再試行します。

DIではAudioModule・AudioResourceRuntime・ResourceJobの実体をsingletonで登録し、同じ実体を
IExecutionModuleへ登録します。ResourceManagerにも同じAudioResourceRuntimeを渡してください。
登録から起動・ロード・停止までのコードは[サンプル](../../../samples/Lumyte.Resources.Audio.Sample/README.md)にあります。

Host配置ならAudioコマンドはホストへ統合され、CPU処理もCooperativeでPumpします。
Dedicated配置ならAudioの所有スレッドとCPUワーカーを使います。
同期的な所有スレッド待機や、フレームごとのAudio更新配送は行いません。

HotReloadは新しいclip・voiceを同じ所有スレッドで構築し、検証後に一括公開します。
取得済みLeaseは旧voice・clipを保持し、旧値を返すまで破棄しません。
新voiceは停止状態です。再生状態・再生位置の移行やクロスフェードは利用側が明示的に扱います。

AudioModuleの更新は所有スレッドで出力状態を確認し、Unavailableになった世代を失効させます。
BrowserのSuspended／interruptedは再開可能なため世代を失効させません。
復旧は同じAudioModuleと固定endpointを使い、アプリが次の順で実行します。

```csharp
await audio.Devices.RecoverAsync(cancellationToken);
await resources.RebuildRuntimeAsync(["Audio"], cancellationToken);
// 旧世代のLease・ReadScopeを返した後に、旧生成物とdeviceを解放する。
await resources.TrimAsync(cancellationToken);
await audio.Devices.RetireAsync(cancellationToken);
```

Stageは再構築ごとに新しい世代を取得します。構築中にロストした結果は公開せず、
旧clipから新deviceのvoiceを生成することも拒否します。
旧生成物のCleanupは生成元deviceへ送り、旧世代のleaseが残る間はdeviceを破棄しません。
復旧失敗は再試行できます。回復と停止が重なる場合も、factoryの実完了と候補の解放を待ちます。

終了時はロード・監視を停止し、全Reference・Lease・ReadScopeを返してResourceManagerを破棄します。
その間もホストはPumpを続け、AudioがCleanupを受け付ける状態を維持します。
その後ExecutionRuntimeを停止し、AudioModuleの世代失効通知、device解放の順に進みます。
