# Lumyte.Resources.Audio

ResourcesのRuntimeを既存のAudioModuleと接続します。
初回ロードとHotReloadのどちらも同じStageを使用します。

- アプリのデコードStage: 非同期I/O → ResourceJobによるCPU処理 → AudioPcmData。
- AudioClipStage: AudioResourceRuntime → 生成済みAudioDeviceコマンド → 所有スレッドでclip生成。
- AudioVoiceStage: 同じRuntime → 生成済みAudioClipコマンド → 所有スレッドでvoice生成。
- 解放: voice → clipの順で、生成済みのCleanupコマンドを所有スレッドへ配送。

生成完了まではawaitし、公開する値は実際のIAudioClip・IAudioVoiceです。
voiceはclipをResident依存として保持し、PCMはclipへのコピー完了までBuild依存として保持します。
デコード形式・メモリ予算・圧縮ストリームの方針はアプリのStageが定めます。
AudioResourceRuntimeはAudioModuleにDependsOnを宣言し、起動完了後に利用可能になります。
Runtimeはdevice本体を所有せず、clipやvoiceを別の辞書で管理しません。

DIではAudioModule・AudioResourceRuntime・ResourceJobの実体をsingletonで登録し、同じ実体を
IExecutionModuleへ登録します。ResourceManagerにも同じAudioResourceRuntimeを渡してください。
登録から起動・ロード・停止までのコードは[サンプル](../../../samples/Lumyte.Resources.Audio.Sample/README.md)にあります。

Host配置ならAudioコマンドはホストへ統合され、CPU処理もCooperativeでPumpします。
Dedicated配置ならAudioの所有スレッドとCPUワーカーを使います。
同期的な所有スレッド待機や、フレームごとのAudio更新配送は行いません。

HotReloadは新しいclip・voiceを同じ所有スレッドで構築し、検証後に一括公開します。
取得済みLeaseは旧voice・clipを保持し、旧値を返すまで破棄しません。
新voiceは停止状態です。再生状態・再生位置の移行やクロスフェードは利用側が明示的に扱います。

終了時はロード・監視を停止し、全Reference・Lease・ReadScopeを返してResourceManagerを破棄します。
その間もホストはPumpを続け、AudioがCleanupを受け付ける状態を維持します。
その後ExecutionRuntimeを停止し、AudioResourceRuntimeの失効通知、AudioModuleのdevice解放の順に進みます。
このAudio接続は一回のdevice寿命を扱い、同一ExecutionRuntime内のdevice再作成は行いません。
