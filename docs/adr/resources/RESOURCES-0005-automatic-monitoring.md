# ADR-RESOURCES-0005: ロード済み入力のファイル変更監視

- 状態: 提案
- 日付: 2026-10-20

## 背景

ADR-RESOURCES-0002の変更通知契約へ実ファイルの編集を接続する。
未ロードのファイル更新で通知queueや再確認処理を増やさず、rename・削除・通知欠落も扱いたい。

## 決定

FileResourceSourceはStreamを開くrootと同じ場所の変更通知をChangesから提供する。
内部のFileResourceChangeSourceがFileSystemWatcherでrootを再帰監視する。
取得元と監視元を別々に登録する公開クラス・DI APIは設けない。
Managerから渡されたIResourceWatchTargetsの最新集合に含まれる入力のみを通知queueへ入れる。
新たにロードされた入力は同じsubscriptionで対象となり、グラフから除かれた入力は対象外になる。
Build依存の実体が回収されても、親の入力来歴が残る間は監視対象である。
直接WatchAsync(token)を使用する場合のみ、root内の全入力を通知する。

対象ファイルの編集は正規化したIDを通知する。対象に関わるdirectory操作・削除・renameでは再走査する。
Windows/macOSではcase aliasを考慮して対象を照合し、再走査で元のIDを検証する。
通知queueの上限超過とnative buffer overflowもRescanへ集約する。
Rescanは追跡中の入力Revisionを確認するだけで、未ロードのファイルを取り込まない。
通知有効化後の初期RescanとManagerの新規入力公開後の確認で、監視開始の隙間を補う。

DIでは`UseSource<FileResourceSource>()`だけでStream提供と監視が接続される。Managerが監視の開始・停止を所有し、
利用側は手動でStart/StopやReloadAsyncを呼ばない。失敗と結果はManager.HotReloadから観測する。
通知は変更のhintであり、Revisionの検証・debounce・有限再試行はADR-RESOURCES-0002のworkerが担当する。
各enumerationがnative watcherを所有し、キャンセルまたはenumeratorの破棄で解放する。
rootの移動・置換にはwatcherの再作成が必要である。
Sourceはscratch書き込みを行わないため、予約名やsnapshotディレクトリの除外は設けない。

## APIと契約

比較元はこのPRの依存ブランチ。以下は追加APIの抜粋。

```diff
+namespace Lumyte.Resources.FileSystem
+{
+    public sealed class FileResourceSource : IResourceSource
+    {
-        public FileResourceSource(FileResourceSourceOptions options);
+        public FileResourceSource(FileResourceSourceOptions options, FileResourceChangeSourceOptions? changeOptions = null);
+        public IResourceChangeSource Changes { get; }
+    }
+}
```

## 検討した代替案

独立した監視元の注入は、Streamの取得元と異なるroot・ID体系を組み合わせられる。
取得元に監視の選択を集約し、Managerがその購読を管理する。

root内の全イベントをManagerへ渡すと未ロードのファイルもqueueを消費する。
ファイル単位でnative watcherを作ると、多数の入力でOSリソースと更新処理が増える。
再帰watcherを共有し、queueへ入れる前に最新の入力集合で絞り込む。

## 結果と影響

ロード済み入力の編集を自動HotReloadへ接続できる。root自体の寿命は利用側が保証する。
OS側の監視はroot全体に及ぶが、利用側のqueueと再構築はロード済み入力に限定する。

## 検証方針

FileResourceSourceだけを渡したManagerの自動更新と旧Lease保持、
実ファイルの編集・rename・削除・復帰、未追跡入力の除外、対象集合の追加、通知過多、
初期再走査、直接読み込みが通知を生成しないこと、監視のキャンセルと解放を検証する。

## 関連ADR

- [ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)
- [ADR-RESOURCES-0004](RESOURCES-0004-source-access.md)
