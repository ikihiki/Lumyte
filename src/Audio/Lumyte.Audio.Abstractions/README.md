# Lumyte.Audio.Abstractions

バックエンドが直接実装する `IAudioDevice`、`IAudioClip`、`IAudioVoice` と PCM 形式・再生状態・音量バスの共通契約です。[設計 ADR](../../../docs/adr/audio/AUDIO-0001-audio-system.md) に契約と使用例を記載しています。

デバイスの生成はバックエンド固有 API で行い、生成済みデバイスを `IAudioDevice` として利用側へ渡します。`CreateClip` は非空の有限 float PCM をコピーし、`CreateVoice` は同じデバイスの clip を借用します。Master／Music／Effects の gain と voice gain は乗算します。

制御・状態参照・解放はデバイスの所有スレッドで行います。操作はバックエンドへ受理順に送られ、`State` は `Update()` のスナップショットです。`Update()` を止めても音声出力は独立して進みます。

voice → clip → device の順で解放します。voice が残る clip と、子が残る device の Dispose は拒否します。リソースローダーが所有する clip は直接解放せず、voice 解放後に lease を返し、manager がキャッシュを解放するまで device を保持します。

初期実装は常駐 mono／stereo clip の再生、一時停止、停止、ループと音量バスです。取得・デコード・先読み・継続供給はリソースシステムの責務とし、その接続 API は未実装です。`Failed` は後続の非同期供給失敗のための状態で、現在の同期失敗は例外で通知します。
