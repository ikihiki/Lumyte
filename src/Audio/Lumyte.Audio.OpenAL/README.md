# Lumyte.Audio.OpenAL

Silk.NET.OpenAL 2.23.0 で OpenAL の device／context／buffer／source を扱います。具象デバイスが `IAudioDevice`、内部の buffer／source 所有者が `IAudioClip`／`IAudioVoice` を直接実装します。

```csharp
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.OpenAL;

using var device = new OpenALDevice(maxVoices: 64);
using IAudioClip clip = device.CreateClip(new AudioFormat(48000, 1), samples);
using IAudioVoice voice = device.CreateVoice(clip, AudioBus.Effects);
voice.Play();
// ゲームループを継続し、各フレームで device.Update() を呼ぶ。
// 再生終了・画面離脱などでスコープを終了する。
```

OpenAL Soft の Native ライブラリはアプリ／ホストが用意します。Linux では `libopenal1`、Windows ではプロセスのアーキテクチャに合う `OpenAL32.dll` をランタイムの検索先へ配置してください。この NuGet パッケージは Native バイナリを同梱しません。

所有スレッドで各操作前に context を current にします。同じスレッドに複数 device を構成できますが、外部コードから OpenAL の context と source を操作しないでください。初期化失敗時は部分生成物を解放します。

float PCM は [-1, 1] に clamp して PCM16 に変換します。mono／stereo のデバイス形式への変換とミックスは OpenAL に任せます。3D 減衰は使わず、voice gain に Master と所属バスの gain を乗算します。量子化と平滑化は Browser と一致しません。

`ALC_EXT_disconnect` がある環境では切断を `Unavailable` として返します。切断後の `ResumeOutputAsync` は `NotSupportedException` となり、子を解放して device を再生成します。拡張がない環境では切断検出を保証しません。解放失敗後に context が終了済みなら Dispose の再試行だけを許します。

[実行サンプル](../../../samples/Lumyte.Audio.Sample/README.md) で契約を確認できます。検証済み: Linux x64／OpenAL Soft の null driver。物理出力の可聴品質、Windows・ARM64 の実機実行は未検証です。
