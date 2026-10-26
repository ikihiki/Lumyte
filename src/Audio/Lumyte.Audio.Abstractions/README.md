# Lumyte.Audio.Abstractions

バックエンドが直接実装する `IAudioDevice`、`IAudioClip`、`IAudioVoice` と PCM 形式・再生状態・音量バスの共通契約です。[設計 ADR](../../../docs/adr/audio/AUDIO-0001-audio-system.md) に契約と使用例を記載しています。

デバイスの生成はバックエンド固有 API で行い、生成済みデバイスを `IAudioDevice` として利用側へ渡します。`CreateClip` は非空の有限 float PCM をコピーし、`CreateVoice` は同じデバイスの clip を借用します。Master／Music／Effects の gain と voice gain は乗算します。

制御・Update・解放はデバイスの所有スレッドで行います。voiceの`State`／`FramePosition`は直近の`Update()`のスナップショットで、Gain／Loopとともに任意のスレッドから読み取れます。getterは保存済みの値だけを返し、`Update()`を止めても音声出力は独立して進みます。

所有スレッドの選択、ジョブからの要求配送、非同期処理後の復帰と必要な同期は利用者の責任です。このパッケージはスレッド実行機構や自動配送を提供しません。バックエンド固有の同期条件は各バックエンドのREADMEに従ってください。

voice → clip → device の順で解放します。voice が残る clip と、子が残る device の Dispose は拒否します。リソースローダーが所有する clip は直接解放せず、voice 解放後に lease を返し、manager がキャッシュを解放するまで device を保持します。

常駐mono／stereo clipの再生、一時停止、停止、Seek、ループと音量バスを扱います。`Seek(long framePosition)`はPCMフレーム単位で`0..clip.FrameCount`を指定し、再生中・一時停止中・停止中のモードを保持します。端点の扱いは[共通契約](../../../docs/adr/audio/AUDIO-0001-audio-system.md#seekと位置の観測)を参照してください。ファイル取得とWAVEデコードは任意の`Lumyte.Resources.Audio`が担当し、継続供給はこのAPIに含みません。`Failed`は非同期供給失敗のための予約状態で、現在の同期失敗は例外で通知します。

`IAudioDevice.Invalidate()`は所有スレッドで利用不能を不可逆に記録します。冪等なmanaged状態の変更だけを行い、生存中deviceでは例外を投げず、native呼び出しや解放は行いません。生成と制御は以後拒否し、既存voice／clip／deviceのCleanupは維持します。device serviceは状態確認の例外で世代を失効させた場合もこれを呼び、元instanceを持つラッパーまで失効を伝えます。
