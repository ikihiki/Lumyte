# WebAudioと型付きコマンド

```sh
dotnet workload install wasm-tools
dotnet publish samples/Lumyte.Audio.Execution.Browser.Sample -c Release -o artifacts/audio-execution-browser
```

公開先wwwrootをHTTPで配信し、ボタンを押します。実際のWebAudioDeviceをHost上で非同期生成し、同じHostでclip・voice生成、再生、一時停止、停止、解放を配送します。専用スレッドとThreadPoolは使用しません。

ユーザー操作でResumeOutputAsyncを直接開始し、その完了まではAudioの通常phaseを止めます。起動・終了のPumpとブラウザのイベントループは継続します。
