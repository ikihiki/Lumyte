# Lumyte.Audio.OpenAL

Silk.NET.OpenAL 2.23.0 で OpenAL の device／context／buffer／source を扱います。具象デバイスが `IAudioDevice`、内部の buffer／source 所有者が `IAudioClip`／`IAudioVoice` を直接実装します。

```csharp
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.OpenAL;

using var device = new OpenALDevice(maxVoices: 64);
using IAudioClip clip = device.CreateClip(new AudioFormat(48000, 1), samples);
using IAudioVoice voice = device.CreateVoice(clip, AudioBus.Effects);
voice.Seek(clip.FrameCount / 2);
voice.Play();
// ゲームループを継続し、各フレームで device.Update() を呼ぶ。
// 再生終了・画面離脱などでスコープを終了する。
```

OpenAL Soft の Native ライブラリはアプリ／ホストが用意します。Linux では `libopenal1`、Windows ではプロセスのアーキテクチャに合う `OpenAL32.dll` をランタイムの検索先へ配置してください。この NuGet パッケージは Native バイナリを同梱しません。

nativeライブラリはBCLのNativeLibraryで読み込み、Silk.NETのdeps.json／runtimesディレクトリ独自探索を使用しません。標準のアプリ／システム検索先へ配置します。Linuxはlibopenal.so.1→libopenal.so、Windowsはopenal32.dll→soft_oal.dll、macOSはシステムOpenAL.framework→libopenal.dylibの順に探します。ALとALContextはそれぞれ取得したlibrary handleを所有し、初期化失敗時も返却します。Linux x64のNativeAOT公開とnull driverでの生成・再生・解放も検証しています。

各 device とその子は、生成した所有スレッドから操作します。所有スレッド外からの制御は例外で拒否します。実行スレッドの選択、ジョブからの要求配送、非同期処理後の復帰は利用者が管理してください。

各操作前に `alcMakeContextCurrent` で context を current にします。current context はプロセスで共有されるため、利用者は複数 device を含む生成・制御・解放の呼び出し全体を直列化し、外部の OpenAL 呼び出しとも競合させないでください。同じスレッドに複数 device を構成できますが、異なる所有スレッドでの並行操作は保証しません。所有スレッドの一致検査だけでは、この排他は保証しません。外部コードからこのバックエンドの context と source を直接操作しないでください。

初期化失敗時は部分生成物を解放します。`maxVoices` はバックエンドの管理上限であり、OpenAL の source 上限より大きい場合もあります。Native の割り当て失敗は例外で通知し、既存 voice の制御・解放と、解放後の新規割り当てを継続できます。

`Seek`は秒数ではなくPCMフレーム位置を受け取り、OpenALのsample offsetで移動します。状態と位置は`device.Update()`で保存し、`FramePosition`のgetterはnative呼び出しを行いません。clip末尾の指定はnativeの範囲外offsetへ渡さず、[共通の端点規則](../../../docs/adr/audio/AUDIO-0001-audio-system.md#seekと位置の観測)で扱います。

float PCM は [-1, 1] に clamp して PCM16 に変換します。変換用のshort配列はArrayPoolから借り、実サンプル数だけをnativeへコピーして返します。mono／stereo のデバイス形式への変換とミックスは OpenAL に任せます。3D 減衰は使わず、voice gain に Master と所属バスの gain を乗算します。量子化と平滑化は Browser と一致しません。

`ALC_EXT_disconnect` がある環境では切断を `Unavailable` として返します。検出した切断は不可逆で、生成・再生制御・Update・Resumeは `AudioDeviceUnavailableException` になります。旧voice・clipのDisposeは引き続き許可し、最後に旧deviceを解放します。旧contextをcurrentにできない場合、子のNative回収はcontext破棄に委ねます。拡張がない環境では切断検出を保証しません。解放失敗後にcontextが終了済みならDisposeの再試行だけを許します。

[実行サンプル](../../../samples/Lumyte.Audio.Sample/README.md) で契約を確認できます。検証済み: Linux x64／OpenAL Soft の null driver。物理出力の可聴品質、Windows・ARM64 の実機実行は未検証です。

[回帰テスト](../../../tests/Audio/Lumyte.Audio.OpenAL.Tests/README.md) は Native の source 枯渇からの回復を確認します。

voiceのState／FramePosition／Gain／Loop getterはbackendのlock内でcached値を読み、任意のスレッドから呼べます。native／JavaScriptの操作は行いません。setter・再生制御・Update・解放は所有スレッドで実行します。clipのFormat／FrameCountは不変のmetadataです。
