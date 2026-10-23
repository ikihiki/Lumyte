# Lumyte.Resources.Audio

既定のResourceRuntimeがAudioのStageをCPUジョブへ配送し、AudioResourceRuntimeが生成と解放をAudioModuleの所有スレッドへ接続します。
初回ロードとHotReloadのどちらも同じStageを使用します。

- AudioDecodeStage: 既定RuntimeのCPUジョブでSourceの非同期I/Oを待ち、WAVEをデコードしてAudioPcmDataを返す。
- AudioClipStage: AudioResourceRuntimeで現在のdevice世代を取得 → 型付きAudioDeviceコマンド → 所有スレッドでclip生成。
- AudioVoiceStage: 同じ手順で世代を取得し、clipの生成元との一致を確認してvoice生成。
- 解放: voice → clipの順で、生成済みのCleanupコマンドを所有スレッドへ配送。

生成完了まではawaitし、公開する値は実際のIAudioClip・IAudioVoiceです。
voiceはclipをResident依存として保持し、PCMはclipへのコピー完了までBuild依存として保持します。
標準のWaveAudioDecoderはRIFF WAVEのmono/stereo PCM8/16/24/32とfloat32を扱います。
圧縮形式・ストリーミング・巨大ファイルのチャンク分割は独自Stageで追加します。
入力長が既知なら最大1 MiBを初期容量として確保し、読み取り中の配列拡張を減らします。
これは事前確保の上限で、入力サイズの制限やMemory予算ではありません。全入力とデコード後PCMはメモリへ保持します。
AudioResourceRuntimeはAudioModuleにDependsOnを宣言し、起動完了後に利用可能になります。
RuntimeはAudioModule.Devicesの世代と可用性をResourcesへ公開し、clipやvoiceを別の辞書で管理しません。
UseAudioAsyncは取得した正確な世代を依存として記録し、deviceのleaseをcontextへ渡します。
StageのReleaseAsyncが子資源を解放した後にleaseを返し、解放失敗時は同じ世代を保持して再試行します。

DIは[Audio専用の登録API](../Lumyte.Resources.Audio.DependencyInjection/README.md)から行います。
`services.AddAudioResources<TSource>("audio")`が本体のSource→PCM→Clip→VoiceとRuntimeを接続します。
DIなしでは`AudioModule`、`AudioResourceRuntime`、汎用の`ResourceRuntime`を同じExecutionRuntimeへ渡します。
ResourceManagerの実行先にはResourceRuntimeを、device世代の依存先にはAudioResourceRuntimeを渡します。

```csharp
var audio = new AudioModule();
var audioResources = new AudioResourceRuntime(audio);
var runtime = new ResourceRuntime();
var execution = new ExecutionRuntime([audio, audioResources, runtime]);
var resources = new ResourceManager("audio", source,
    [new AudioDecodeStage(), new AudioClipStage(audioResources), new AudioVoiceStage(audioResources)],
    memory, runtime, [audioResources]);
```

復旧やスレッド制約の検証は[結合テスト](../../../tests/Audio/Lumyte.Resources.Audio.IntegrationTests/README.md)にあります。

Host配置ならAudioコマンドはホストへ統合され、CPU処理もCooperativeでPumpします。
Dedicated配置ならAudioの所有スレッドとCPUワーカーを使います。
StageのBuildAsync・ReleaseAsyncと通常のawait後の継続は汎用Runtimeが配送し、I/O待ちの間はCPU実行枠を返します。
Stageの通常のawaitはRuntimeが設定した実行先へ戻ります。
ConfigureAwait(false)はこの配送から外れるため、Stage内では使用しません。
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
// 旧世代のLease・Snapshotを返した後に、旧生成物とdeviceを解放する。
await resources.TrimAsync(cancellationToken);
await audio.Devices.RetireAsync(cancellationToken);
```

Stageは再構築ごとに新しい世代を取得します。構築中にロストした結果は公開せず、
旧clipから新deviceのvoiceを生成することも拒否します。
旧生成物のCleanupは生成元deviceへ送り、旧世代のleaseが残る間はdeviceを破棄しません。
復旧失敗は再試行できます。回復と停止が重なる場合も、factoryの実完了と候補の解放を待ちます。

終了時はロード・監視を停止し、全Reference・Lease・Snapshotを返してResourceManagerを破棄します。
その間もホストはPumpを続け、AudioがCleanupを受け付ける状態を維持します。
その後ExecutionRuntimeを停止し、AudioModuleの世代失効通知、device解放の順に進みます。
