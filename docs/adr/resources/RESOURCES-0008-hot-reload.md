# ADR-RESOURCES-0008: 入力変更によるHotReload

- 状態: 提案
- 日付: 2026-10-23

## 背景

論理ハンドルと内容世代の基盤から、変更通知・自動監視・再構築を独立してレビューする。

## 決定

IResourceSource.Changesから同じ取得元の変更通知を取得し、Managerが購読を所有する。
ResourceReloadServiceは通知の重複排除、debounce、最大集約時間、保留数上限、有限再試行を扱う。
変更された入力とその親を再構築し、Revisionと公開状態を検証して候補グラフを一括公開する。
失敗・競合時は旧公開状態を維持し、既存LeaseとReadScopeは旧内容を保持する。
未追跡入力と無変更通知では再構築せず、回収済みBuild依存の入力来歴は保持する。
Managerは終了時に監視と自動再構築を停止・joinしてからリソースを解放する。
DIではUseSourceで読み込みと監視を接続し、ConfigureHotReloadで集約・再試行を設定する。
実ファイルのFileSystemWatcherアダプターと自動更新sampleは別の監視PRで扱う。

## APIと契約

ResourceManager.HotReloadから結果・失敗・Completionを観測する。
IResourceChangeSource.WatchAsyncはロード中・公開中の入力集合を受け取る。
通知はhintとして扱い、公開前にSourceのRevisionを確認する。
ReloadAsyncとReloadAllAsyncは内部のデバッグ用コマンドで、公開APIに含めない。
変更通知のないSourceでは監視処理を開始しない。Enabled=falseでも購読を無効にできる。

## 検討した代替案

世代管理と同時に導入する構成では、ハンドルの所有権と監視処理の寿命を分けてレビューできない。
通知を受けるたびに即座に再構築すると、重複通知と連続編集で不要な処理が増える。

## 結果と影響

世代管理のみのPRはバックグラウンド監視を持たず、本PRで自動更新を追加する。
監視の起動停止、通知集約、再試行の実装は本PRで独立して見直せる。
今回の分割では既存の実行方式を変更しない。

## 検証方針

一括公開、旧LeaseとReadScopeの保持、失敗・競合時のrollback、変更範囲だけの再構築、
通知集約、再構築中の追加通知、再試行、監視対象の追従、自動開始・終了順を検証する。

## 関連ADR

- [ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)
