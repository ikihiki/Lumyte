# ADR-AUDIO-0001: バックエンドが共通インターフェースを直接実装するオーディオ機構

- 状態: 採用
- 更新日: 2026-10-26
- 日付: 2026-10-09

## 背景

Lumyte は Windows、Linux、Browser を対象とする C# のゲームエンジンである。効果音の同時再生、長い BGM のストリーミング、音量カテゴリごとの調整をゲーム側から共通の契約で扱いたい。OS のデバイス API や Browser の再生許可を直接利用コードへ持ち込むと、再生状態、スレッド、リソース解放の扱いが環境ごとに分散する。

本ADRは常駐PCMの共通契約とSilk.NET.OpenAL／Web Audio APIバックエンドを記録する。Resourcesによる取得・デコードと所有スレッドへの配送は[AUDIO-0002](AUDIO-0002-execution-and-resources.md)が扱う。継続的なPCM供給は対象に含めない。

## 決定

### 対象範囲と依存関係

再生制御の共通契約と、それを実装してデバイスへ出力するバックエンドを分離する。対象は常駐モノラル／ステレオPCM、効果音、再生・一時停止・停止・Seek、ループ、固定のMaster／Music／Effectsバスとする。ストリーミングBGM、3D音響、録音、任意のDSPグラフ、ピッチ変更、サンプル精度の予約再生は含めない。

[リポジトリのフォルダ構成](../0002-repository-layout.md) と [グラフィックデバイスの設計](../graphics/GRAPHICS-0001-graphics-device.md) に従い、共通契約のプロジェクト・パッケージ・名前空間を `Lumyte.Audio.Abstractions` とし、`src/Audio/Lumyte.Audio.Abstractions/`へ配置する。バックエンドは同じ Audio カテゴリへ置き、Abstractions を参照する。

バックエンドの具象デバイスが `IAudioDevice`、具象 clip が `IAudioClip`、具象 voice が `IAudioVoice` を直接実装する。デバイスの `CreateClip` と `CreateVoice` は、そのバックエンドの具象リソースを共通インターフェースとして返す。共通契約にはラッパーや内部 driver への転送層を置かず、ミックス・状態管理・出力はバックエンドが契約を満たす形で実装する。

起動部分が選択したバックエンド固有の API でデバイスを生成し、利用側へ `IAudioDevice` として渡す。共通 factory、バックエンドの自動選択、共通のデバイス生成設定は設けない。同時 voice 上限などの初期化設定はバックエンド固有 API で指定し、上限超過時の拒否は共通契約とする。Windows／Linux は `Lumyte.Audio.OpenAL.OpenALDevice` を `new OpenALDevice(string? deviceName = null, int maxVoices = 64)` で生成する。Silk.NET.OpenAL 2.23.0 を使用し、OpenAL Soft の実体はホスト側で用意する。Browser は `Lumyte.Audio.Browser.WebAudioDevice.CreateAsync(string moduleUrl, int maxVoices = 64, CancellationToken cancellationToken = default)` で生成し、Web Audio API の AudioContext・AudioBuffer・AudioBufferSourceNode・GainNode を使用する。

Abstractions は Engine、Graphics、Settings、Resources、特定の DI コンテナーに依存しない。設定の読み込み・保存は構成側、音声データの取得・デコード・非同期供給・先読み・ストリーミング用バッファの管理はリソースシステムとその音声アダプターが担当する。オーディオ側は供給された PCM の消費、ミックス、再生制御、デバイス出力を担当する。共通 Audio と共通 Resources は互いに依存せず、両者の接続は統合アダプターに置く。

共通Resourcesの型付きPipelineとleaseは常駐clipの取得に使う。独立したストリームセッションと音声デバイスのリアルタイム消費との接続は別の契約を必要とする。

### 公開 API

以下は主要な宣言である。制御APIは構築時の所有スレッドから呼び、状態と位置のsnapshotをUpdateで反映する。voiceのState／FramePosition／Gain／Loopのgetterは保存済みの値を任意のスレッドから読める。

```diff
+namespace Lumyte.Audio.Abstractions
+{
+    // SampleRate は正の Hz、Channels は 1 または 2。既定値は無効。
+    public readonly record struct AudioFormat(int SampleRate, int Channels);
+    public enum AudioBus { Master, Music, Effects }
+    public enum AudioPlaybackState { Stopped, Playing, Paused, Completed, Failed }
+    public enum AudioOutputState { Suspended, Running, Unavailable }
+
+    public interface IAudioDevice : IDisposable
+    {
+        // デバイスと再生許可の状態。Running でも実際に聞こえることは保証しない。
+        public AudioOutputState OutputState { get; }
+        // 所有スレッドで通知・状態を更新。音声処理をフレーム周期で進めない。
+        public void Update();
+        // Browser ではユーザー操作から呼ぶ。拒否・デバイス不在は例外で返す。
+        // 完了は出力再開の確認。所有スレッドで await 完了まで次の制御を行わない。
+        public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default);
+        // 入力をコピーし、バックエンド実装の clip を返す。
+        // 有限 float の interleaved PCM、フレーム数は正数。
+        // 長さは Channels の倍数。形式不正は ArgumentException。
+        public IAudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples);
+        // clip は同じ device が生成した有効なもの。バックエンド実装の voice を返す。
+        // 同時 voice 上限到達は InvalidOperationException。
+        public IAudioVoice CreateVoice(IAudioClip clip, AudioBus bus = AudioBus.Effects);
+        public void Invalidate();
+        // Master は全体、Music／Effects はその内側。gain は有限の [0, 1]、初期値 1。
+        public void SetBusGain(AudioBus bus, float gain);
+        // 生存中の clip／voice があれば状態を変えず InvalidOperationException。
+        // 子の解放後に出力処理を停止し、デバイスを解放。成功後は冪等。
+        public void Dispose();
+    }
+
+    public interface IAudioClip : IDisposable
+    {
+        public IAudioDevice Device { get; }
+        public AudioFormat Format { get; }
+        // チャンネルごとのサンプル数ではなく、PCM フレーム数。
+        public long FrameCount { get; }
+        // voice が参照中なら InvalidOperationException。解放は冪等。
+        public void Dispose();
+    }
+
+    public interface IAudioVoice : IDisposable
+    {
+        // 初期状態 Stopped。Update が最後に取り込んだ状態。
+        public AudioPlaybackState State { get; }
+        // Updateが最後に取り込んだPCMフレーム位置。初期値0。
+        public long FramePosition { get; }
+        // 有限の [0, 1]、初期値 1。バスと Master の gain を乗算する。
+        public float Gain { get; set; }
+        // 初期値 false。clip の末尾から先頭へ戻って再生する。
+        public bool Loop { get; set; }
+        // Stopped／Pausedは設定した位置から、Completedは先頭から。Playingは冪等。
+        // Failed は後続の非同期供給失敗用。初期実装の同期失敗は例外。
+        public void Play();
+        // Playing を Paused にする。その他の状態では何もしない。
+        public void Pause();
+        // 停止を要求し、Stopped は次回 Update で反映。次回 Play は先頭から。
+        public void Stop();
+        // 0..clip.FrameCountのPCMフレーム位置。再生／一時停止／停止を保つ。
+        public void Seek(long framePosition);
+        // 出力参照を終了し、clip の借用を解除。冪等。
+        public void Dispose();
+    }
+
+}
```

`CreateVoice` も Master 指定を拒否する。未定義 enum、不正な数値、別 device のリソースは受理前に拒否し、副作用を残さない。破棄済みリソースの制御は `ObjectDisposedException`、契約外の状態操作は `InvalidOperationException` とする。Invalidateは所有スレッドで利用不能を不可逆に記録する冪等な操作で、native呼び出しや解放を行わない。生存中deviceでは例外を投げず、その後の生成・制御を拒否してCleanupを維持する。バックエンドはこれらのインターフェースを直接実装し、生成物の所属デバイスを検証する。同じバックエンド型でも別デバイスの clip を拒否する。具象型へのキャストを利用側へ要求しない。生成失敗は例外とし、部分生成物を解放する。OpenAL のエラーは InvalidOperationException、Browser の API エラーは JSException として通知する。

### バックエンド固有の生成 API

```diff
+namespace Lumyte.Audio.OpenAL
+{
+    // OpenAL Soft はホストが用意する。生成と制御は同じ所有スレッド。
+    public sealed class OpenALDevice : IAudioDevice
+    {
+        // deviceName=null は既定出力。maxVoices は正数、初期化失敗は例外。
+        public OpenALDevice(string? deviceName = null, int maxVoices = 64);
+    }
+}
+namespace Lumyte.Audio.Browser
+{
+    public sealed class WebAudioDevice : IAudioDevice
+    {
+        // JS モジュールを import。相対 URL は Browser location に対して解決。
+        // maxVoices は正数。生成後、ユーザー操作から ResumeOutputAsync を呼ぶ。
+        public static Task<WebAudioDevice> CreateAsync(string moduleUrl,
+            int maxVoices = 64, CancellationToken cancellationToken = default);
+    }
+}
```

共通インターフェースの全メンバーは各具象デバイスが実装する。Native 同梱方針、JS 配信、検証済み環境は各バックエンドの README に記載する。

### 使用例

以下は共通 API の利用例である。実行可能な検証サンプルは `samples/Lumyte.Audio.Sample/` と `samples/Lumyte.Audio.Browser.Sample/` に置く。生成済みの `IAudioDevice` は構成側が用意し、制御と Dispose は同じ所有スレッドで行う。非同期処理の継続はそのスレッドへ戻す。

#### 効果音の生成・再生・音量調整

デコード済み PCM を受け取る最小例。`samples` は有限 float の interleaved PCM とする。実際のゲームでは取得・デコードをリソース側へ任せる。

```csharp
using Lumyte.Audio.Abstractions;

// audio は構成側で生成した IAudioDevice を借用する。
// format、samples はリソース側が用意する。
using IAudioClip clip = audio.CreateClip(format, samples);
using IAudioVoice effect = audio.CreateVoice(clip, AudioBus.Effects);

audio.SetBusGain(AudioBus.Master, 0.8f);
audio.SetBusGain(AudioBus.Effects, 0.5f);
effect.Gain = 0.75f; // 最終 gain は 0.8 × 0.5 × 0.75 = 0.3。
effect.Play();

// ここでスコープを終了せず、ゲームループから毎回呼ぶ。
// 音声そのものは Update と独立して進む。
audio.Update();
if (effect.State == AudioPlaybackState.Completed)
{
    // 再生完了をゲーム側へ反映する。
}

// 別の入力イベントから要求する操作の例。
effect.Pause();
// 音声処理が Pause を適用した後、次の Update で Paused を確認する。
// その後の Play は一時停止位置から再開する。
// effect.Play();
// effect.Stop(); // 次の Play は先頭から再生。

// ゲーム終了時にスコープを抜ける。
// using の逆順で effect → clip を破棄する。
// 構成側はすべての子を解放した後に audio.Dispose() を呼ぶ。
```

`Play` の直後に `State` を見て開始や完了を判断しない。状態は後続の `Update` で確認する。連続した操作も受理順に処理される。clip のループ再生では、停止中に `effect.Loop = true` を指定してから `Play` を呼ぶ。

#### リソース lease を保持して再生する

[実行基盤との統合](AUDIO-0002-execution-and-resources.md)では、ResourceManagerがAudioClipを共有し、利用側が各再生のAudioVoiceを所有する。生成・制御・解放はラッパーから所有スレッドへ配送するため、利用側のawait継続をAudioの所有スレッドへ固定する必要はない。Host配置では待機中もPumpを続ける。

```csharp
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Resources;

// 起動済みのresourcesを構成側から借用する。
var key = new ResourceKey<AudioClip>("audio/effects/confirm");
using var reference = await resources.AcquireAsync(key, cancellationToken);
ResourceLease<AudioClip> lease = reference.Acquire();
AudioVoice voice = await lease.Value.CreateVoice(AudioBus.Effects, cancellationToken);
await voice.Play(cancellationToken);
// ゲームループを継続し、完了・画面離脱・終了までvoiceとleaseを保持する。
await voice.Destroy();
lease.Dispose();

// 未使用キャッシュの解放時点で構成側が呼ぶ。
await resources.TrimAsync(cancellationToken);
```

同じclipから複数voiceを作る場合も、全voiceのDestroy完了までleaseを保持する。voiceの解放に失敗した場合はleaseを返さず、解放を再試行する。Managerが所有するclipにDestroyClipを直接呼ばない。上の断片は成功時の順序を示しており、Play直後にDestroyまで進めると再生は終了する。

#### Browser の出力再開とストリーミングの接続

Browser ではユーザーの開始ボタンなどの操作ハンドラーから次を呼ぶ。所有スレッド上で完了を待つ間は他の制御 API を呼ばず、拒否やデバイス不在の例外は呼び出し側で扱う。

```csharp
await audio.ResumeOutputAsync(cancellationToken);
// 所有スレッドへ戻った後、準備済みの voice を再生する。
voice.Play();
```

ストリーミング BGM は供給・接続 API が未確定のため、架空のメソッドを C# 使用例へ追加しない。統合側の利用順序は次のとおりとする。

1. リソース側で音声アセットの lease と、再生専用の供給セッションを取得する。
2. リソース側でデコードと先読みを進め、オーディオへ準備済み PCM を接続する。
3. オーディオ側で Music バスへ再生し、ゲームループから状態を反映する。
4. 終了時はオーディオの参照を解除し、リソース側の供給セッションの終了を待ってから lease を返す。

先読み容量や I/O の取消はリソース側で設定・処理する。接続 API が確定した後、その契約に基づく C# 使用例を追加する。

### 再生状態とコマンド

所有スレッドから受理順にバックエンド API を呼ぶ。共通層に独自のコマンドキューは設けない。呼び出しの成功は可聴完了を意味しない。状態の公開スナップショットは Update で反映するが、Play／Pause は内部の最新状態で判断する。生成時に同時 voice 上限を検査し、暗黙の voice stealing は行わない。

clip の末尾、またはリソース側から通知された EOF までの PCM を再生し終えたら Completed、供給側や出力の継続不能な失敗は Failed とする。一時停止中は再生位置を進めない。clip のループは末尾から先頭へ戻る。ストリーミングの巻き戻し・ループ要求はリソース側との接続契約で定め、オーディオがファイルやデコーダーを操作しない。

OpenAL は source の状態を Update で取得する。Browser は再生ごとに SourceNode を生成し、停止・一時停止時に終了ハンドラーを解除する。自然終了は現在の SourceNode に一致する場合だけ受理し、古い終了通知が新しい再生を Completed に戻さない。初期実装の同期 API 失敗は例外で通知し、Failed の非同期通知は継続供給の契約確定後に実装する。

### Seekと位置の観測

`Seek(long framePosition)`はclip内のPCMフレーム位置を指定する。チャンネル数を掛けたサンプル要素数や出力deviceのフレーム数ではない。範囲は`0..clip.FrameCount`で、範囲外は`ArgumentOutOfRangeException`とし、既存の位置・状態を変更しない。単位を整数フレームに統一し、秒数とfloat offsetの往復による丸めを避ける。秒へ変換する場合は`(double)voice.FramePosition / clip.Format.SampleRate`を使う。

| 操作時の状態 | Seek後の動作 |
| --- | --- |
| Playing、位置が末尾より前 | 指定位置から再生を続ける |
| Paused／Stopped、位置が末尾より前 | 指定位置を保持し、次のPlayで開始する |
| Completed、位置が末尾より前 | Stoppedへ戻し、次のPlayで指定位置から開始する |
| Loop=trueで末尾を指定 | 位置を0とし、再生・一時停止・停止を保つ |
| Loop=false、Playingで末尾を指定 | 位置をFrameCountとしCompletedになる |
| Loop=false、Paused／Stoppedで末尾を指定 | モードを保ち、次のPlayでCompletedになる |

Stopは位置を0へ戻す。自然終了の位置はFrameCountで、CompletedからのPlayは先頭から再生する。SeekはGain／Loopを変更せず、別voiceやclipの位置にも影響しない。非ループの末尾でCompletedとなった後のPlayも先頭から再生する。

FramePositionとStateは同じUpdateで取り込み、各getterはbackendのlock内で保存済みの値を読む。getter自身はnative／JS呼び出しや配送を行わない。複数getterの呼び出し間にUpdateが入る可能性はあり、一括した原子的snapshotを返すAPIではない。位置はbackendの観測位置であり、スピーカーの可聴位置や予約再生の締め切りを保証しない。

OpenALはsample offsetを使用し、末尾そのものをnativeへ設定しない。Browserは再生中のSeekでSourceNodeを交換するため、その分のJS/native割り当てがある。旧ノードのonendedを解除し、既に保留中の通知も現在ノードとの一致で判定する。旧ノード停止後の再作成失敗はStoppedとして例外を返し、次のPlayで指定位置から再試行できる。Seekの入力精度と、音が切り替わる時刻の精度は別の契約とする。

### スレッド、PCM とストリーミング

各デバイスとその子の制御・Update・解放は生成時の所有スレッドで行う。voiceの保存済みsnapshotとclipの不変metadataの参照は任意のスレッドで行える。backendを直接使う場合、必要な同期と非同期処理後の復帰は利用側が保証する。所有スレッド外からの制御は例外で拒否する。実行キューと自動配送は任意のLumyte.Audio.Executionが担当する。

OpenALは各操作前に `alcMakeContextCurrent` でcontextを選択する。current contextはプロセスで共有されるため、複数デバイスを含む生成・制御・解放の呼び出し全体を利用者が直列化し、外部のOpenAL呼び出しとも競合させない。所有スレッドの一致検査だけでは、この排他は保証しない。

音声出力はゲームの Update と独立した周期で進む。リソースシステムは取得・デコード・先読みを非同期に進め、上限付きバッファから準備済み PCM を供給する。オーディオ側は独自のデコード worker やストリーミング用リングバッファを持たない。デバイス出力とミックスに必要な作業バッファはオーディオ側で事前確保する。

音声コールバックは準備済み PCM を待機せずに取り出す。ファイル I/O、await、任意の利用者コールバック、ログ出力、ロック待ち、ヒープ割り当てを行わない。供給境界には「PCM あり」「一時的な供給不足」「EOF」「失敗」を区別できる契約を必要とする。具体的な型と API はリソースのストリーミング ADR で定め、本 ADR では ReadAsync やバッファ容量を持つオーディオ API を公開しない。

サンプルレート変換とモノラル／ステレオ変換はバックエンドのミックス境界で行い、デバイスの出力形式へ変換する。音量を乗算して float で加算し、最終出力への変換は各バックエンドの出力実装に委ねる。非有限 PCM はクリップ作成時に拒否し、供給された PCM に含まれる場合は Failed とする。Browser の再生中の gain 変更には 5 ms のランプを使う。生成時と停止・一時停止中は再生前に gain を直接適用し、ミュート状態からの開始で音が漏れないようにする。OpenAL は source gain を設定し、その平滑化は実装側に委ねる。初期実装は OpenAL へ clamp 済みの PCM16 を渡すため、量子化がある。最終ミックスの clamp と変換は各出力実装に委ね、バックエンド間のビット一致は保証しない。

供給不足は不足部分を無音で埋め、消費できた PCM だけでソース位置を進める。供給不足で Completed にしない。Paused／Suspended 中は再生位置を保持する。リソース側の先読み上限、供給の停止・再開、巻き戻し時に古い PCM を混ぜない世代管理は接続契約で定める。

### 所有権と終了処理

デバイスは生成した clip と voice の所属と生存状態を追跡し、呼び出し側が子の解放責任を持つ。voice は clip を借用する。デバイスの Dispose は生存中の子があれば拒否し、暗黙に子を破棄しない。clip は複数 voice から共有できる。リソースの音声Stageで生成した clip は manager が解放責任を持ち、利用側は voice の破棄まで lease を保持する。使用中の clip をReleaseAsyncで解放せず、既存の参照中破棄の契約に従って拒否する。

ストリーミングではリソース側が取得元、デコーダー、非同期処理、供給バッファとその終了処理を所有する。共有アセットの lease と、再生ごとの独立したカーソル・供給セッションを分ける。再生ごとのセッションを共有キャッシュへそのまま格納する契約は採用しない。具体的なセッション生成・保持 API はリソース側で定める。

voiceのDisposeは所有スレッドでsourceを停止・解放し、PCM参照を解除してから戻る。音声サンプルを消費するリアルタイムcallbackからは呼ばない。供給 PCM は消費終了までリソース側で有効に保ち、参照中にバッファを再利用しない。統合側はオーディオの参照を解除した後、リソース側の供給セッションを終了し、非同期処理の取消・終了を待ち、最後に lease を返す。lease 返却自体には待機や I/O を持ち込まない。

終了時は voice の出力参照を解除してから、供給セッションと lease、resource manager と残りの clip を解放し、最後にデバイスを破棄する。音声StageのReleaseAsyncがデバイスを必要とする間は、デバイスを保持する。停止失敗時も残りの安全に実行できる終了処理を試み、参照解除が確認できない PCM を解放しない。Browser の AudioContext.close は非同期である。同期 Dispose は子の解放と close の開始までを保証し、閉鎖完了は保証しない。close の拒否は console.error へ通知する。

### プラットフォームと出力状態

| 環境 | 共通の要求 | 後続 ADR で決定する事項 |
| --- | --- | --- |
| Windows | デバイス出力と停止・再開、x64／ARM64 | 出力ライブラリ、Native ABI、RID |
| Linux | 同じ PCM／voice 契約、x64／ARM64 | 音声サーバーとの接続、デバイス選択 |
| Browser | ユーザー操作からの再開、タブ停止の状態反映 | Web Audio／AudioWorklet、Wasm と worker の共有方式 |

対応表は対象環境の要求を示す。検証した環境はバックエンド README に記載する。Browser の自動再生制限を無音の成功として隠さず Suspended とする。出力の消失は Unavailable とし、自動的に別デバイスへ切り替えたり再生位置をリセットしたりしない。Browser の復旧は明示的な ResumeOutputAsync で試みる。OpenALはALC_EXT_disconnectがある場合に切断を検出する。失効後の再生制御とResumeOutputAsyncはAudioDeviceUnavailableExceptionとなり、再生には別deviceの生成が必要になる。拡張がない場合の切断検出は保証しない。キャンセル時は呼び出しの待機を取り消し、復旧が先に成立した場合に出力停止へ巻き戻す保証はしない。

## 検討した代替案

### ゲームから各 OS の音声 API を直接使う

環境固有の機能を使いやすいが、所有権・状態・再生許可・同時再生制限の扱いが分散する。共通の再生契約を優先し、固有のデバイス設定はバックエンドへ分離する。

### 共通クラスが内部バックエンドへ転送する

共通層へ状態や検証をまとめやすいが、具象リソースの寿命と内部 driver の境界が増える。Graphics と同様にバックエンドが共通インターフェースを直接実装し、生成物を返す構成を採用する。

### すべての音声をメモリへ展開する

効果音には適するが、長い BGM のメモリ消費と起動時の待ち時間が増える。短い PCM clip と有界バッファの stream を併用する。

### オーディオ側でストリーミングを管理する

音声専用に調整しやすいが、取得・非同期供給・バッファ・寿命管理がリソースシステムと重複する。これらをリソース側へ集約し、オーディオ側は準備済み PCM の消費に限定する。

### ゲームのフレーム更新で PCM を供給する

実装は単純になるが、描画や GC によるフレーム遅延が音切れへ直結する。音声周期と I/O を独立させ、Update は制御結果の反映だけを担当する。

### 最初から汎用 DSP グラフと 3D 音響を公開する

拡張性は高いが、グラフ更新・ノード寿命・空間化の契約まで必要になる。初期の再生と出力境界を検証してから別 ADR で追加する。

## 結果と影響

- ゲーム側は環境によらず再生と音量カテゴリを扱える。
- PCM、デコード、出力の責務が分かれ、ファイル形式やバックエンドを追加できる。
- voice 上限とリソース側の stream バッファ上限により保持メモリを制限する。一方、供給不足や生成拒否への対応が必要になる。
- 非同期反映のため、制御呼び出し直後のState／FramePositionは要求と一致しない場合がある。
- オーディオの参照解除とリソース側の非同期供給終了を分けて扱う。リアルタイム安全性と lease を返す順序の検証が必要になる。
- 各バックエンドのミキサーと Browser 連携に実装コストがかかる。性能と実環境の可聴品質は別途測定する。

## 検証方針

実装時は共通インターフェースを実装する偽のデバイス、有限 PCM、準備済み PCM の偽の供給境界を用い、再生・停止・一時停止・Seek・EOF・ループ・失敗の状態遷移、操作順序、上限超過、古い通知の失効を検証する。フレーム更新を遅延させても音声処理が独立して進むことを確認する。各バックエンドで同じ契約の検証を実行し、返り値がそのバックエンドの実装であること、別デバイスの clip を拒否すること、生存中の子があるデバイスの破棄を拒否することを確認する。

PCM のチャンネル境界、形式変換、gain の乗算、clamp、非有限値、短い読込、供給不足と EOF の区別を検証する。clip の参照中破棄、再生セッションのカーソル分離、参照解除後のリソース側の取消、出力中の voice／device 破棄、バックエンド停止失敗で use-after-free や供給セッションの残留が起きないことを確認する。継続供給に関わる項目は、その契約と実装の追加時に検証する。

各実環境ではデバイス不在・切断・復旧、Browser の初回操作・自動再生拒否・タブ停止を検証する。出力コールバックの割り当て、処理時間、供給不足数、開始遅延、メモリ上限を測定し、音量変更とループ境界の可聴ノイズを確認する。数値目標は測定条件とともにバックエンド ADR で定める。

### 初期実装の確認結果

Linux x64／.NET 10 で OpenAL Soft の null driver による実バックエンド検証と、Chromium 上の発行済み Wasm サンプルの検証を実行した。共通の引数・所有権・voice 上限・再生状態の契約に加え、自然終了と clip 境界を越えるループを確認した。OpenAL は所有スレッド外からの操作を拒否することを確認した。

OpenALの回帰テストでは、Native source枯渇後の既存voiceの解放・再割り当てを確認した。割り当て直後にエラーを検査し、無効handleへのcleanupで後続操作のエラー状態を汚さない。

Browser は OfflineAudioContext の実レンダリングでステレオのチャネル分離と gain 適用後の波形を照合し、停止した旧 SourceNode の完了通知が新しい再生に影響しないことも確認した。消音状態で生成・開始したvoiceの全サンプルが0であること、複数周回後にループを解除してpause／resumeしてもclip内の位置が維持されることを回帰検証した。物理スピーカー、Windows／ARM64 の実機、デバイス切断・復旧、可聴品質・性能は未検証である。

## 別途決定する事項

- OpenAL Soft のアプリへの RID 別同梱方針と、実機の可聴品質・デバイス切断復旧。
- 非同期の失敗情報・供給不足の診断 API。
- リソース側のストリーミング設計: 対応コーデック、デコード、先読み上限、供給セッション、巻き戻し・ループ、PCM の受け渡しと解放。
- Browser の AudioWorklet、Wasm の共有メモリ、worker の構成と終了方式。
- デバイス選択、マルチチャンネル、3D 音響、DSP、予約再生と他の時計との同期。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [ADR-GRAPHICS-0001: グラフィックデバイスの共通契約](../graphics/GRAPHICS-0001-graphics-device.md)
