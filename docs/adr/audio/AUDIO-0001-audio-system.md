# ADR-AUDIO-0001: 再生制御とバックエンドを分離するオーディオ機構

- 状態: 提案
- 日付: 2026-10-09

## 背景

Lumyte は Windows、Linux、Browser を対象とする C# のゲームエンジンである。効果音の同時再生、長い BGM のストリーミング、音量カテゴリごとの調整をゲーム側から共通の契約で扱いたい。OS のデバイス API や Browser の再生許可を直接利用コードへ持ち込むと、再生状態、スレッド、リソース解放の扱いが環境ごとに分散する。

現在の main には Audio の実装も採用済みの設計もない。本 ADR は最初のオーディオ機構の設計案であり、バックエンドや外部ライブラリの選定、実装完了を表さない。

## 決定

### 対象範囲と依存関係

共通の再生制御と、デバイス出力を行うバックエンドを分離する。初期対象はモノラル／ステレオの PCM、効果音、ストリーミング BGM、再生・一時停止・停止、ループ、固定の Master／Music／Effects バスとする。3D 音響、録音、任意の DSP グラフ、ピッチ変更、サンプル精度の予約再生は後続設計とする。

[リポジトリのフォルダ構成](../0002-repository-layout.md) に従い、実装時に `src/Audio/Lumyte.Audio/` と必要なバックエンドを同じ Audio カテゴリへ置く。共通の名前空間・パッケージ名は `Lumyte.Audio` とし、Engine、Graphics、Settings、ファイルローダー、特定の DI コンテナーに依存しない。構成側がバックエンドをコンストラクター注入する。設定の読み込み・保存は構成側、音声データの取得・デコード・非同期供給・先読み・ストリーミング用バッファの管理はリソースシステムとその音声アダプターが担当する。オーディオ側は供給された PCM の消費、ミックス、再生制御、デバイス出力を担当する。共通 Audio と共通 Resources は互いに依存せず、両者の接続は統合アダプターに置く。

リソース管理の既存案 ADR-RESOURCES-0001（PR #25）は共有読み込みと lease を扱い、継続供給の契約はまだ定めていない。今回の責務分担はリソース側のストリーミング設計への要求として記録し、既存 API だけで対応できるとは扱わない。

### 公開 API 案

比較元は main `5bf75e46ec8320a889315a0707ca16ea29e54b34`。以下は新規追加の宣言案であり、実装済み API ではない。すべての制御 API は構築時の所有スレッドから呼び、処理済みの状態と通知を `Update` で反映する。

```diff
+namespace Lumyte.Audio
+{
+    // SampleRate は正の Hz、Channels は 1 または 2。既定値は無効。
+    public readonly record struct AudioFormat(int SampleRate, int Channels);
+    public enum AudioBus { Master, Music, Effects }
+    public enum AudioPlaybackState { Stopped, Playing, Paused, Completed, Failed }
+    public enum AudioOutputState { Suspended, Running, Unavailable }
+
+    public sealed class AudioSystem : IDisposable
+    {
+        // backend は借用。一つの System に専属。maxVoices は正数、既定 64。
+        public AudioSystem(IAudioBackend backend, int maxVoices = 64);
+        // デバイスと再生許可の状態。Running でも実際に聞こえることは保証しない。
+        public AudioOutputState OutputState { get; }
+        // 所有スレッドで通知・状態を更新。音声処理をフレーム周期で進めない。
+        public void Update();
+        // Browser ではユーザー操作から呼ぶ。拒否・デバイス不在は例外で返す。
+        // 完了は出力再開の確認。所有スレッドで await 完了まで次の制御を行わない。
+        public ValueTask ResumeOutputAsync(CancellationToken cancellationToken = default);
+        // 入力をコピー。有限 float の interleaved PCM、フレーム数は正数。
+        // 長さは Channels の倍数。形式不正は ArgumentException。
+        public AudioClip CreateClip(AudioFormat format, ReadOnlySpan<float> samples);
+        // clip は同じ System の有効なもの。上限到達は InvalidOperationException。
+        public AudioVoice CreateVoice(AudioClip clip, AudioBus bus = AudioBus.Effects);
+        // Master は全体、Music／Effects はその内側。gain は有限の [0, 1]、初期値 1。
+        public void SetBusGain(AudioBus bus, float gain);
+        // backend の出力処理を停止し、全 voice と clip を解放。
+        // 借用 backend 自体は破棄しない。二度目以降は何もしない。
+        public void Dispose();
+    }
+
+    public sealed class AudioClip : IDisposable
+    {
+        public AudioFormat Format { get; }
+        // チャンネルごとのサンプル数ではなく、PCM フレーム数。
+        public long FrameCount { get; }
+        // voice が参照中なら InvalidOperationException。解放は冪等。
+        public void Dispose();
+    }
+
+    public sealed class AudioVoice : IDisposable
+    {
+        // 初期状態 Stopped。Update が最後に取り込んだ状態。
+        public AudioPlaybackState State { get; }
+        // 有限の [0, 1]、初期値 1。バスと Master の gain を乗算する。
+        public float Gain { get; set; }
+        // 初期値 false。clip の末尾から先頭へ戻って再生する。
+        public bool Loop { get; set; }
+        // Stopped／Completed は先頭から、Paused は停止位置から。Playing は冪等。
+        // Failed は再利用しない。
+        public void Play();
+        // Playing を Paused にする。その他の状態では何もしない。
+        public void Pause();
+        // 出力を停止し State を Stopped にする。再生位置は次回 Play 時に先頭へ戻す。
+        public void Stop();
+        // 出力参照を終了し、clip の借用を解除。冪等。
+        public void Dispose();
+    }
+
+}
```

`CreateVoice` も Master 指定を拒否する。未定義 enum、不正な数値、別 System のリソースは受理前に拒否し、副作用を残さない。破棄済みリソースの制御は `ObjectDisposedException`、契約外の状態操作は `InvalidOperationException` とする。`IAudioBackend` の具体的な公開メンバーは、出力コールバックと Browser の連携方式を確定するバックエンド ADR で定める。本案の公開 API はその契約確定後に採用する。

### 再生状態とコマンド

制御側の操作を上限付きキューへ格納し、音声処理のブロック境界で適用する。受理は即時反映や可聴完了を意味しない。キューが満杯なら `InvalidOperationException` とし、コマンドを黙って落とさない。同じ voice への操作は受理順に処理する。生成時に上限を検査し、暗黙の voice stealing は行わない。

clip の末尾、またはリソース側から通知された EOF までの PCM を再生し終えたら Completed、供給側や出力の継続不能な失敗は Failed とする。一時停止中は再生位置を進めない。clip のループは末尾から先頭へ戻る。ストリーミングの巻き戻し・ループ要求はリソース側との接続契約で定め、オーディオがファイルやデコーダーを操作しない。

状態の反映はキュー受理順を保存し、古い完了通知が後続の Play を Completed に戻さない世代識別を持つ。通知キューも有界とし、終端状態と失敗を保持して Update が取り込めるようにする。詳細なコマンド容量と失敗情報の公開 API は採用前に確定する。

### スレッド、PCM とストリーミング

音声出力はゲームの Update と独立した周期で進む。リソースシステムは取得・デコード・先読みを非同期に進め、上限付きバッファから準備済み PCM を供給する。オーディオ側は独自のデコード worker やストリーミング用リングバッファを持たない。デバイス出力とミックスに必要な作業バッファはオーディオ側で事前確保する。

音声コールバックは準備済み PCM を待機せずに取り出す。ファイル I/O、await、任意の利用者コールバック、ログ出力、ロック待ち、ヒープ割り当てを行わない。供給境界には「PCM あり」「一時的な供給不足」「EOF」「失敗」を区別できる契約を必要とする。具体的な型と API はリソースのストリーミング ADR で定め、本 ADR では ReadAsync やバッファ容量を持つオーディオ API を公開しない。

サンプルレート変換とモノラル／ステレオ変換は共通のミックス境界で行い、バックエンドへデバイスの出力形式を渡す。音量を乗算して float で加算し、最終出力を [-1, 1] に clamp する。非有限 PCM はクリップ作成時に拒否し、供給された PCM に含まれる場合は Failed とする。音量のブロック間変更は短いランプで接続し、期間は実装時の可聴検証で定める。

供給不足は不足部分を無音で埋め、消費できた PCM だけでソース位置を進める。供給不足で Completed にしない。Paused／Suspended 中は再生位置を保持する。リソース側の先読み上限、供給の停止・再開、巻き戻し時に古い PCM を混ぜない世代管理は接続契約で定める。

### 所有権と終了処理

System は生成した clip と voice を所有し、voice は clip を借用する。clip は複数 voice から共有できる。リソースローダーで生成した clip は manager が解放責任を持ち、利用側は voice の破棄まで lease を保持する。使用中の clip を Unload で解放せず、既存の参照中破棄の契約に従って拒否する。

ストリーミングではリソース側が取得元、デコーダー、非同期処理、供給バッファとその終了処理を所有する。共有アセットの lease と、再生ごとの独立したカーソル・供給セッションを分ける。再生ごとのセッションを共有キャッシュへそのまま格納する契約は採用しない。具体的なセッション生成・保持 API はリソース側で定める。

voice の Dispose は未処理コマンドを失効させ、音声処理が PCM 参照を手放してから戻る。音声スレッドからは呼ばない。供給 PCM は消費終了までリソース側で有効に保ち、参照中にバッファを再利用しない。統合側はオーディオの参照を解除した後、リソース側の供給セッションを終了し、非同期処理の取消・終了を待ち、最後に lease を返す。lease 返却自体には待機や I/O を持ち込まない。

終了時は voice／System の出力参照を解除してから、供給セッションと lease、resource manager、借用 backend の順に各所有者が解放する。停止失敗時も残りの安全に実行できる終了処理を試み、参照解除が確認できない PCM を解放しない。Dispose のエラー集約方式はバックエンド契約とともに定める。

### プラットフォームと出力状態

| 環境 | 共通の要求 | 後続 ADR で決定する事項 |
| --- | --- | --- |
| Windows | デバイス出力と停止・再開、x64／ARM64 | 出力ライブラリ、Native ABI、RID |
| Linux | 同じ PCM／voice 契約、x64／ARM64 | 音声サーバーとの接続、デバイス選択 |
| Browser | ユーザー操作からの再開、タブ停止の状態反映 | Web Audio／AudioWorklet、Wasm と worker の共有方式 |

対応表は要求を示し、現在の対応実績ではない。Browser の自動再生制限を無音の成功として隠さず Suspended とする。出力の消失は Unavailable とし、自動的に別デバイスへ切り替えたり再生位置をリセットしたりしない。復旧は明示的な ResumeOutputAsync で試みる。キャンセル時は呼び出しの待機を取り消し、復旧が先に成立した場合に出力停止へ巻き戻す保証はしない。

## 検討した代替案

### ゲームから各 OS の音声 API を直接使う

環境固有の機能を使いやすいが、所有権・状態・再生許可・同時再生制限の扱いが分散する。共通の再生契約を優先し、固有のデバイス設定はバックエンドへ分離する。

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
- キューと stream バッファの上限により保持メモリを制限する。一方、供給不足やコマンド拒否への対応が必要になる。
- 非同期反映のため、制御呼び出し直後の State は要求と一致しない場合がある。
- オーディオの参照解除とリソース側の非同期供給終了を分けて扱う。リアルタイム安全性と lease を返す順序の検証が必要になる。
- 共通ミキサーと Browser 連携に実装コストがかかる。本 PR は設計文書だけであり、性能や対応環境は未検証である。

## 検証方針

実装時は偽の backend、有限 PCM、準備済み PCM の偽の供給境界 を用い、再生・停止・一時停止・EOF・ループ・失敗の状態遷移、コマンド順序、上限超過、古い通知の失効を検証する。フレーム更新を遅延させても音声処理が独立して進むことを確認する。

PCM のチャンネル境界、形式変換、gain の乗算、clamp、非有限値、短い読込、供給不足と EOF の区別を検証する。clip の参照中破棄、再生セッションのカーソル分離、参照解除後のリソース側の取消、出力中の voice／System 破棄、バックエンド停止失敗で use-after-free や 供給セッションの残留が起きないことを確認する。

各実環境ではデバイス不在・切断・復旧、Browser の初回操作・自動再生拒否・タブ停止を検証する。出力コールバックの割り当て、処理時間、供給不足数、開始遅延、メモリ上限を測定し、音量変更とループ境界の可聴ノイズを確認する。数値目標は測定条件とともにバックエンド ADR で定める。

## 別途決定する事項

- バックエンドライブラリ、IAudioBackend の公開契約、Native ABI、パッケージと RID。
- コマンド容量、通知保持方式、失敗情報・供給不足の診断 API。
- リソース側のストリーミング設計: 対応コーデック、デコード、先読み上限、供給セッション、巻き戻し・ループ、PCM の受け渡しと解放。
- Browser の AudioWorklet、Wasm の共有メモリ、worker の構成と終了方式。
- デバイス選択、マルチチャンネル、3D 音響、DSP、予約再生と他の時計との同期。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
