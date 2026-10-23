# ADR-AUDIO-0002: 実行基盤とResourcesによるAudioの生成・制御・復旧

- 状態: 採用
- カテゴリ: audio

## 決定

Audio固有の実装はCore実行基盤と共通Resources Runtimeから分離する。
Lumyte.Audio.Executionが実backendのDevice／Clip／Voiceを型付きコマンドへ接続し、
Lumyte.Resources.AudioがSource→WAVEデコード→Clip→Voiceを本体のStageとして提供する。
DI登録はLumyte.Resources.Audio.DependencyInjectionのAddAudioResourcesでまとめる。
サンプルの型やfake backendへ本体は依存しない。

AudioVoiceは元IAudioVoice参照一本を持つreadonly structで、メソッドから送信コードを生成する。
Play／Stop／Destroyと実体生成は固定の所有スレッドへ送る。更新はowner自身が駆動する。
State／Gain／Loopはbackendがlockで保護したcached値を呼び出し元で読む。
実行層はClip／Voiceの一覧やID表を持たず、所属・借用・寿命の確認はbackendが行う。

非同期I/OはSourceで待機し、WAVEの有限なCPUデコードはResourceJobへ送る。
標準対応はRIFF WAVE mono/stereoのPCM8/16/24/32とfloat32とし、不正入力を公開しない。
圧縮codec、ストリーミングと巨大ファイルのチャンク分割は別Stageで追加する。
PCMはclipコピーまでBuild依存、clipはvoice解放までResident依存として保持する。
生成は実体の完成を待ち、解放はvoice→clip→deviceの順で所有スレッド上に実行する。

AudioModule.Devicesは長寿命serviceと交換可能なdevice世代を分ける。
構築ごとに正確な世代leaseを取得し、service identity・versionをResourceManagerへ記録する。
Unavailableは旧世代を失効させ、Suspended／interruptedは同じ世代を維持する。
復旧は同じownerと静的portでdeviceを再作成し、Resourcesを明示的に再構築する。
旧clipと新deviceの混在、生成中に失効した候補の公開を拒否する。
旧deviceは子資源と世代leaseの返却後に所有スレッドで退役し、解放失敗は再試行する。
復旧と停止が重なってもfactoryの実完了と候補の解放を待つ。

BrowserはHost／Cooperativeへ統合する。非同期生成とuser gesture内のresumeを待つ間も
ホストループを維持し、音声供給callbackからmanaged jobの完了を待たない。
HotReloadも同じStageを利用し、新voiceは停止状態で公開する。再生状態の移行は利用側が扱う。

## 検証

デコードとDI登録は単体テスト、生成・配送・復旧・終了は専用の結合テストに置く。
Host／Dedicated、DIあり／なしと実OpenALを検証し、ロストはfake backendで決定的に注入する。
物理デバイスの抜き差しと実機の遅延分布は別途検証する。

## 関連

- [Core実行基盤](../core/CORE-0003-execution-and-jobs.md)
- [共通Resources Runtime](../resources/RESOURCES-0009-runtime-execution.md)
- [Audio本体](../../../src/Audio/Lumyte.Resources.Audio/README.md)
- [DI登録](../../../src/Audio/Lumyte.Resources.Audio.DependencyInjection/README.md)
