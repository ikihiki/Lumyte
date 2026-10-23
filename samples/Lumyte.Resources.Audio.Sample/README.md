# Resources Audio Runtime sample

非同期Source読み込み → 型付きCPUジョブ → 所有スレッドでclip・voice生成 → 公開・再生 → 依存順解放を実行します。
初期化・生成の実体が完成するまで待機し、キャンセル済みジョブとデコード失敗の非公開も検証します。
既定のテストbackendは全native操作のスレッドと、voice → clip → deviceの解放順を検証します。

```sh
dotnet run --project samples/Lumyte.Resources.Audio.Sample -c Release
dotnet run --project samples/Lumyte.Resources.Audio.Sample -c Release -- --di
dotnet run --project samples/Lumyte.Resources.Audio.Sample -c Release -- --host
dotnet run --project samples/Lumyte.Resources.Audio.Sample -c Release -- --host --di
ALSOFT_DRIVERS=null dotnet run --project samples/Lumyte.Resources.Audio.Sample -c Release -- --openal
```

`--openal`は実OpenAL backend、`--host`はAudioとCPUを一つのホストへ統合します。
各実行は新しいprocessで起動し、生成済みAudioのstatic endpointは一回だけ接続します。

DIありでは、AudioModule・AudioResourceRuntime・ResourceJobをsingleton登録し、IExecutionModuleへ同じ実体を渡します。
ExecutionRuntimeはDIが収集したModuleを受け取ります。
AddResourcesでSource・Stageを登録し、AddResourceRuntimeDependencyで同じAudioResourceRuntimeを接続します。
DIなしでは同じ実体をExecutionRuntimeとResourceManagerのconstructorへ渡します。
実際の登録コードとホストのPumpは[Program.cs](Program.cs)を参照してください。

ホストはPrepareOnCurrentHost → StartAsyncを進め、開始待ち・ロード待ち・終了待ちの間もPumpを続けます。
このconsole sampleの同期WaitはPumpを含み、単純なTask.Waitではありません。
Browserではこの待機ループをコピーせず、ブラウザのフレームループからPumpしながらawaitします。
Reference・Leaseを返し、ResourceManagerの終了をPumpしてからExecutionRuntimeを停止します。
`--reload`でSourceの変更通知から同じロード経路を再実行し、voice差し替え・旧Lease保持・不正デコード時の旧公開保持を検証します。
`--host`・`--di`・`--openal`と組み合わせられます。実ファイルの監視adapterは後続PRです。
