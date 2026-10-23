# ADR-RESOURCES-0009: 非同期ロードとRuntimeの実行先

- 状態: 採用
- 日付: 2026-10-23

## 決定

Runtimeはハンドル・世代管理の直後に導入し、HotReloadと個別デバイスの接続に先行させる。
実行基盤のPR #41を前提とし、Resources内に独自のThreadやTask.Runワーカーを作らない。

Sourceの非同期I/Oを待ち、有限なCPU処理は型付きResourceJobからExecutionRuntimeへ送る。
Stageは非同期workflowとして依存を読み込み、CPU handler内ではawaitや別リソースのロードを行わない。
Resourcesのロード依存グラフとExecutionのJobGraphは分離し、動的な依存探索と公開はResourceManagerが管理する。
Native実体の生成・解放は個別モジュールが所有スレッドへ配送する。生成は実体完成を待つ。
Stageの非同期継続そのものにスレッド親和性は仮定しない。

IResourceRuntimeDependencyは安定したprovider identity、version、可用性、失効watermarkを提供する。
Managerへ明示的に登録し、構築ごとに実際に使ったRuntime世代を記録して公開前にも検証する。
Stageは長寿命serviceを保持し、交換されるdeviceの世代を構築ごとに取得する。
旧世代の解放は元の実体と実行先を使い、新しいdeviceへ付け替えない。

Resident依存の失効は旧Leaseにも伝播し、Build依存で完成した独立値は失効させない。
ResidentのRuntime依存は世代の構築完了時に平坦化し、Leaseの読み取りで一時確保やグラフ再探索を行わない。
RebuildRuntimeAsyncは保持されたhandleに新世代を公開するtransactional再構築を使う。
失敗・競合時は旧公開を保持し、失効済み候補を公開しない。

AudioとGraphicsの具体的なdevice service、Stage、デコード、DI登録、復旧は各専用PRで提供する。
HotReloadは通知をまとめて同じ再構築基盤とStageを呼び出し、別の生成経路を作らない。

## 起動と終了

DIありでもなしでもModule実体を構築・収集し、PrepareOnCurrentHost → StartAsync → ロードの順に進む。
ResourceManagerへ登録するproviderとStageが使うserviceのidentityを一致させる。
Host／Cooperative構成ではCPUジョブとowner処理をホストへ統合し、await中もホストループのPumpを維持する。
終了時は監視とロードを停止し、Reference・Lease・ReadScopeを返してResourceManagerの解放を完了する。
元ownerがCleanupを受け付ける間に解放し、最後にExecutionRuntimeを停止する。

## 検証

Runtime世代・失効・復旧、公開競合、Resident／Build伝播、解放再試行を共通テストで検証する。
個別モジュールの専用PRは同じStageによる実体生成・制御とHost／Dedicatedの配置を検証する。
HotReload PRは差し替え、旧Lease保持、失敗時の旧公開保持と終了順を検証する。

## 関連ADR

- [ハンドルと世代](RESOURCES-0002-generations-reload.md)
- [実行基盤](../core/CORE-0003-execution-and-jobs.md)
