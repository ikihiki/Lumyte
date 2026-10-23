# Lumyte.Audio.Browser

Web Audio API の AudioContext／AudioBuffer／AudioBufferSourceNode／GainNode を .NET の JS interop で扱います。`WebAudioDevice` が `IAudioDevice` を直接実装します。

```csharp
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Browser;

using WebAudioDevice device = await WebAudioDevice.CreateAsync("./lumyte-audio.js");
using IAudioClip clip = device.CreateClip(new AudioFormat(48000, 1), samples);
using IAudioVoice voice = device.CreateVoice(clip, AudioBus.Music);
// ユーザー操作ハンドラーから呼び、完了後に所有スレッドで再生する。
await device.ResumeOutputAsync();
voice.Play();
// ゲームループで device.Update() を呼び、再生中はこのスコープを保持する。
```

`wwwroot/lumyte-audio.js` をアプリから配信してください。パッケージでは `contentFiles/any/net10.0/wwwroot/` に格納します。実行サンプルは明示的に JS を publish 出力へコピーします。

clip 作成時に interleaved float PCM を JS 所有の AudioBuffer へコピーします。一時停止で経過した AudioContext 時刻から offset を保存し、再開時は新しい SourceNode を生成します。再生中にループを変更する場合も、その時点の clip 内の位置を保存します。停止時に旧 onended を外し、古い完了通知を新しい再生に反映しません。voice 生成時は現在のバス音量を初期値として設定し、停止・一時停止中の音量変更は次の再生前に適用します。再生中の音量変更には 5 ms のランプを使用します。

AudioContext が suspended／interrupted なら `Suspended`、closed なら `Unavailable` を返します。ユーザー操作から Resume を呼び、自動再生制限を解除してください。キャンセルは待機の終了だけで、JS の resume を巻き戻しません。同期 Dispose は子の解放確認と context.close の開始までを保証します。close の非同期失敗は console.error へ通知します。

[実行サンプル](../../../samples/Lumyte.Audio.Browser.Sample/README.md) は開始ボタンから .NET と JS の両方を実行します。検証済み: Linux x64 Chromium／.NET 10 Wasm。継続供給と AudioWorklet はリソース側の契約確定後に追加します。

voiceのState／Gain／Loop getterはbackendのlock内でcached値を読み、任意のスレッドから呼べます。native／JavaScriptの操作は行いません。setter・再生制御・Update・解放は所有スレッドで実行します。clipのFormat／FrameCountは不変のmetadataです。
