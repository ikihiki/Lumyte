# Resources Audio integration tests

出力型で選んだRuntimeからCPUジョブへ配送 → 非同期Source読み込みとWAVEデコード → Audio環境の所有スレッドでclip・voice生成 → 公開・再生 → 依存順解放を実行します。
初期化・生成の実体が完成するまで待機し、キャンセル済みロードとデコード失敗の非公開も検証します。
既定のテストbackendは全native操作のスレッドと、voice → clip → deviceの解放順を検証します。
lease取得と生成を合わせたcommandについて、native生成失敗時のlease返却、世代依存登録失敗後の候補解放、解放失敗時の旧lease保持とTrimによる再試行も検証します。

```sh
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --di
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --host
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --host --di
ALSOFT_DRIVERS=null dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --openal
```

`--openal`は実OpenAL backend、`--host`はAudioとCPUを一つのホストへ統合します。
各実行は新しいprocessで起動し、生成済みAudioのstatic endpointは一回だけ接続します。

DIありでは本体の`AddAudioResources<TestSource>`を使用します。DIなしでも本体のAudioDecodeStage、
AudioClipStage、AudioVoiceStageを使用します。汎用ResourceRuntimeをmanagerの既定実行先とし、
clipとvoiceのstageRuntimesには同じAudioResourceRuntimeを指定します。
AudioResourceRuntimeは汎用RuntimeのExecutorを共有し、Dependenciesでdevice世代の依存先をManagerへ提示します。
StageはBuildAsyncとReleaseAsyncの型付きAudioResourceRuntime引数で選択済み環境を受け取り、解放には生成時に保持したRuntimeと元backendを使用します。
TestSourceはWAVEファイルを供給し、fake backendだけをテスト用に置き換えます。
登録とPumpは[Program.cs](Program.cs)を参照してください。

ホストはPrepareOnCurrentHost → StartAsyncを進め、開始待ち・ロード待ち・終了待ちの間もPumpを続けます。
このテスト実行器の同期WaitはPumpを含み、単純なTask.Waitではありません。
Browserではこの待機ループをコピーせず、ブラウザのフレームループからPumpしながらawaitします。
Reference・Leaseを返し、ResourceManagerの終了をPumpしてからExecutionRuntimeを停止します。
Sourceの変更通知とHotReloadも本体の同じStageを使用します。

`--recover`はテストbackendの出力状態を変更し、一時停止で世代が変わらないこと、ロストによる旧Leaseの失効、復旧失敗後の再試行、`RebuildRuntimeAsync(["Audio"])`による新デバイスへの再構築を検証します。旧clipを新世代へ渡す操作は拒否し、旧Leaseの返却とnative子資源の解放が済むまで旧デバイスを保持します。子資源の解放失敗も注入し、再試行後にだけデバイスを破棄します。生成途中のロストは候補を公開せず解放し、復旧完了commandの容量不足では生成済みデバイスを保持して再試行します。

`--recover-stop`は非同期factoryの完了を遅らせ、同時に呼んだ復旧とRuntime停止が競合しても、候補デバイスを所有スレッドで解放してから停止が完了することを検証します。

`--recover-start-cancel --host`は最初のowner Pump前に起動をキャンセルし、factoryを呼ばずにAudioModuleを停止できることを検証します。

```sh
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --recover
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --recover --host --di
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --recover-stop
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --recover-stop --host --di
dotnet run --project tests/Audio/Lumyte.Resources.Audio.IntegrationTests -c Release -- --recover-start-cancel --host --di
```

復旧検証は`--host`・`--di`と組み合わせられます。`--openal`・`--reload`とは別の実行にしてください。物理出力デバイスの取り外しは行いません。

`--reload`は変更通知による差し替え、旧Lease保持、不正WAVE時の旧公開保持を検証します。
