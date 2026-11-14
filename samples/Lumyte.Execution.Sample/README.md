# Execution typed command sample

引数なしのDemoModuleを登録し、所有スレッド上で初期化・voice生成・Play／Stop／Destroy・周期更新・解放を行うサンプル。DemoVoiceは元のDemoVoiceDataへの参照一本を持つreadonly structで、実際のOpenALを使わず、作成threadと寿命の制約を検証する。

```sh
dotnet run --project samples/Lumyte.Execution.Sample -c Release
dotnet run --project samples/Lumyte.Execution.Sample -c Release -- --di
dotnet run --project samples/Lumyte.Execution.Sample -c Release -- --host
dotnet run --project samples/Lumyte.Execution.Sample -c Release -- --host --di
```

既定では専用threadを使い、`--host` は同じ役割をHostへ統合してCooperativeで動かす。これはBrowserのmanaged実行方式を通常の.NET上で確認する経路であり、WasmやWebAudioを起動するサンプルではない。

DIの登録は次の2行。Runtimeはmodule一覧を受け取り、定義を収集する。

```csharp
services.AddSingleton<IExecutionModule, DemoModule>();
services.AddSingleton<ExecutionRuntime>();
```

[Program.cs](Program.cs)は固定HostでPrepareし、Pumpを回しながらStartとStopを観測する。生成・制御は外側のasync workflowから送り、実体の生成完了を待つ。providerの破棄はStop成功後に行う。[DemoModule.cs](DemoModule.cs)は物理配置と10msの制御更新周期を宣言し、[DemoState.cs](DemoState.cs)と[DemoVoice.cs](DemoVoice.cs)のCoreメソッドからラッパー自身の送信APIが生成される。

## ベンチマーク

```sh
dotnet run --project build -c Release -- --target BenchmarkCore --benchmark-project Lumyte.Execution --benchmark-mode screen
```

Linux x64のNative AOTでも、DI有無とHost／専用threadの4構成を起動から終了まで確認した。

```sh
dotnet publish samples/Lumyte.Execution.Sample -c Release -r linux-x64 -p:PublishAot=true
```

専用threadのwake遅延・p99、実Audio deviceの性能、全threadの割当、Native AOT／Wasmの性能はこの計測の対象外。繰り返し測定の生データと環境を保存し、最適化の判断には別環境の結果も使う。
