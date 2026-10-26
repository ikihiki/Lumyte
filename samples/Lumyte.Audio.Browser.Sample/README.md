# Web Audio オーディオサンプル

`mise exec -- dotnet workload install wasm-tools` の後、`mise exec -- dotnet publish samples/Lumyte.Audio.Browser.Sample -c Release -o artifacts/audio-browser` で発行します。

`python3 -m http.server --directory artifacts/audio-browser/wwwroot 8000` で配信し、Browser で開始ボタンを押してください。再生・一時停止・ループ・自然終了・所有権などを .NET から検証し、成功時に `Web Audio .NET contracts passed.` と表示します。

自動検証には Python の Playwright をインストールし、Chromium を用意します。`python3 tools/audio/verify_browser.py artifacts/audio-browser/wwwroot` で実行します。システムの Chromium を使う場合は `--chromium /usr/bin/chromium` を指定します。

自動検証は開始ボタンの操作、.NET／JS interop の契約検証、OfflineAudioContext によるステレオ PCM と gain の出力波形、停止後の古い完了通知の拒否を確認します。物理スピーカーの可聴品質は検証しません。
