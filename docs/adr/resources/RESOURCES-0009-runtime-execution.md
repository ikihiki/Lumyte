# ADR-RESOURCES-0009: 非同期ロードとRuntimeの実行先

- 状態: 提案
- 日付: 2026-10-23

## 背景

ハンドルと内容世代の次に、実行スレッドやデバイスに依存するリソースの構築を定める。
Audioのclip・voiceの生成と解放は所有スレッドで実行し、デコードやI/Oとは配置を分ける必要がある。
初回ロードだけでなくHotReloadでも同じ制約を満たす。

## 決定

Runtimeをハンドル・世代管理の直後、HotReload・ストリーミング・ファイルSourceより前に導入する。
実行基盤のPR #41を前提とし、Resources内で独自のThreadやTask.Runワーカーを作らない。
Sourceの非同期I/Oを待ち、有限のCPU処理は型付きResourceJobからExecutionRuntimeへ送る。
ResourceJobはIExecutionModuleとしてDIまたは明示的に登録する。
ネイティブオブジェクトの作成は各Runtimeがモジュールの所有スレッドへ配送し、実体の完成を待つ。
Stageの非同期継続そのものにスレッド親和性は仮定しない。

AudioResourceRuntimeはAudioModuleの起動に依存し、clip・voiceを生成済みの型付きコマンドで作成する。
AudioClipStageはPCMをBuild依存としてコピー完了まで保持する。
AudioVoiceStageはclipをResident依存としてvoice解放まで保持する。
実体の破棄も所有スレッドへ送り、Resourcesは生成物の別管理表を持たない。
AudioModuleがdeviceを所有し、ResourcesはStageの出力と依存グラフを所有する。

IResourceRuntimeDependencyはversion・可用性・失効watermark・provider identityを提供する。
構築時に使用versionを記録し、公開前にも検証する。
Resident依存の失効は旧Leaseにも伝播し、Build依存で完成した独立した値は失効させない。
RebuildRuntimeAsyncは保持されたhandleへ新世代を公開する共通のtransactional再構築を使う。
Audio接続は一回のdevice寿命を扱う。device再作成を行うGraphics接続は後段の別PRで追加する。

HotReloadは通知を集約する起動側であり、CPU・Audioの別実行経路を作らない。
同じStageで候補を構築し、入力Revision・Runtime version・公開versionを検証して一括公開する。
失敗・競合では旧公開を維持し、旧Leaseは旧clip・voiceを保持する。
新voiceの再生状態の移行は利用側で行い、自動再生しない。

Host配置とCooperative CPUでは同じ処理をホストへ統合する。
一つのCPU handlerは有限のチャンクとし、ブラウザではホストをブロックせずPumpとawaitを組み合わせる。
Audioのサンプル供給はdevice周期に従い、リソースロードのジョブ完了を待たせない。

## 起動と終了

DIありでもなしでも、Module実体を構築・収集 → PrepareOnCurrentHost → StartAsync → ロードの順とする。
DIのResourceManagerとExecutionRuntimeには同じAudioResourceRuntime実体を渡す。
終了時は監視とロードを停止し、Reference・Lease・ReadScopeを返し、ResourceManagerの解放を完了する。
その間AudioのCleanupを受け付け、必要なHost Pumpを続ける。
最後にExecutionRuntimeを停止し、Runtimeを失効させてからdeviceを解放する。

## 検討した代替案

Stage全体をTask.Runで包むだけでは、native操作の所有スレッド制約を満たせない。
生成直後にplaceholderを返す方式は、実行完了とResourceManagerの公開を分離してしまう。
初回ロードとHotReloadで別の生成コードを持つ方式は、依存とスレッド制約の差異を生む。

## 検証方針

Runtime世代・失効・復旧と公開競合をCoreで検証する。
CPUジョブとAudio生成・解放をDedicated/ThreadPool、Host/Cooperative、DIあり・なしで実行する。
開始前・キャンセル・不正入力で未完成値を公開しないことを確認する。
HotReload PRでは同じ経路での差し替え、旧Lease保持、失敗時の旧公開保持と終了順を検証する。

## 関連ADR

- [ハンドルと世代](RESOURCES-0002-generations-reload.md)
- [実行基盤](../core/CORE-0003-execution-and-jobs.md)
