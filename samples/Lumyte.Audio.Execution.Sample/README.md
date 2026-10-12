# 実際のAudioと実行基盤の接続

OpenAL Softをインストールし、次の各構成を実行します。`--host`なしでは専用スレッド、`--host`ありでは単一スレッドへ統合します。

```sh
dotnet run --project samples/Lumyte.Audio.Execution.Sample
dotnet run --project samples/Lumyte.Audio.Execution.Sample -- --di
dotnet run --project samples/Lumyte.Audio.Execution.Sample -- --host
dotnet run --project samples/Lumyte.Audio.Execution.Sample -- --host --di
```

CIでは`ALSOFT_DRIVERS=null`を設定します。実際のOpenAL context・buffer・sourceを使用し、再生・一時停止・停止の状態、clipの借用中解放拒否、voice→clipの解放、型付きbatchの順序、backendの状態を呼び出し元スレッドで取得を検証します。

所有Hostは起動・生成・終了の非同期待機中もPumpを継続します。DI登録時はデバイスを開かず、ExecutionRuntimeの起動で初めて所有スレッド上で生成します。

現行Silk.NETのloaderがIL3000／IL3002を出すため、この実OpenALサンプルのNativeAOT公開は未対応です。CoreのNativeAOT検証とは区別します。
