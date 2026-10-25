# ADR-RESOURCES-0007: ロード済み入力のファイル変更監視

- 状態: 提案
- 日付: 2026-10-20

## 背景

[HotReload](RESOURCES-0004-hot-reload.md) の変更通知契約へ実ファイルの編集を接続する。root内の全イベントを無条件で流すと、未ロードのファイルやeditorの一時ファイルがqueueを消費する。単一ファイルの書き換えだけでなく、atomic replacement、directoryのrename・削除、OSの通知欠落を扱う必要がある。

[FileSystem Source](RESOURCES-0006-source-access.md) と監視対象のroot・ID体系を一致させ、通知から構築までの経路をResourceManagerの既存の公開・失敗契約へ接続する。

## 決定

### Sourceへの統合

`FileResourceSource.Changes` が、読み込みと同じrootの変更通知を提供する。内部の `FileResourceChangeSource` が `FileSystemWatcher` のイベントを `IAsyncEnumerable<ResourceSourceChange>` へ変換する。取得元と監視元を別々に選ぶ公開クラスやDI登録APIは作らない。

Sourceのconstructorでは設定だけを検証し、native watcherは列挙の開始時に作る。各enumerationが独立したwatcherと通知queueを所有し、キャンセルまたはenumeratorの破棄で解放する。アプリ共通の一つのManagerが購読を保持し、子scopeの終了では監視を停止しない。

DIでは `UseSource<FileResourceSource>()` により読み込みと監視が接続される。DIを使わない場合も同じSourceをManagerへ渡す。Managerは自動HotReloadが有効で `Changes` が存在すると購読を開始し、Resourcesの解放前に停止する。監視を無効にした構成ではnative watcherを作らない。

### 監視対象の選別

Managerは構築中・公開中の入力来歴から `IResourceWatchTargets` を提供する。adapterはイベントごとにその最新の不変集合を読み、queueへ入れる前に対象を選別する。新しい入力のロードに購読の作り直しは不要であり、来歴から除かれた入力は対象外になる。Build依存の値を回収しても、それを使った親の入力来歴が残る間は監視を続ける。

対象ファイルだけでなく、その祖先directoryへの操作も関連イベントとする。WindowsとmacOSではcase aliasを考慮して照合し、再走査ではManagerが保持する元のIDを使う。リンクを含むpath、root外のpath、ResourceIdで表せないpathは個別通知へ変換しない。path情報の取得中に消失・アクセス拒否などで判断できなければ、再走査へ切り替える。

`Changes.WatchAsync(token)` を直接呼ぶ場合は対象集合を渡さないため、root内の入力を広く通知する。Managerが使う対象付きoverloadとは用途が異なる。直接購読した呼び出し側がenumeratorの終了責任を持つ。

### イベントと再走査

変更通知は更新された内容そのものではなく、Revisionを確認するためのhintとする。識別できる通常のファイル編集ではIDを通知し、削除・directory操作・renameでは `ResourceSourceChange.Rescan` へまとめる。renameは旧pathと新pathのいずれかが対象に関係すれば再走査する。WindowsとmacOSのイベントもcase aliasを考慮して再走査とする。

通知queueが満杯の場合は個別通知を捨て、一つのRescanへ置き換える。native buffer overflowもRescanに変換する。Rescan待機中の追加イベントは統合し、queueを無制限に増やさない。Rescanは追跡中の入力Revisionを確認する要求であり、root全体のファイルをロードする要求ではない。

watcherのイベントを有効にしてから初期Rescanを送る。Managerも新しい入力の公開後にRevisionを再確認し、購読開始・対象集合追加と編集の間の隙間を補う。通知の回数・順序・一対一対応は保証しない。Revisionの強度はSourceの契約に従い、メタデータを維持する変更の完全検出は監視によって追加されない。

### 非同期処理とエラー

watcherのcallbackではpathの識別、対象照合、通知queueの更新だけを行う。payloadの読み込み、Stageの構築、Native資源生成は実行しない。queueの `TryWrite` と短い同期区間を使い、consumerの空きを同期的に待たない。channelの継続はcallback上で同期実行させない。

イベントがない間、adapterの列挙はchannelの非同期待機へ入り、専用の実行threadや周期pollを作らない。debounce、再試行、Stageの実行先と一括公開は共通HotReloadとRuntimeの責務である。入力通知を受け取ることと、リソース生成のための所有スレッドを持つことを分離する。

`InternalBufferOverflowException` 以外のwatcher errorは通知列挙を失敗させる。自動構成ではManagerの `HotReload.Completion` と `LastError` から終端エラーを観測できる。adapterは同じwatcherを無期限に再起動しない。構築失敗時の旧公開保持と有限再試行は共通HotReloadの契約に従う。

rootのdirectory自体は購読中に移動・置換しないことを要求する。その寿命が変わる場合は利用側が監視を含む新しい構成を作る。停止済みのManager所有HotReload serviceを再起動する契約は設けない。

### 容量と性能

managed通知queueの既定上限は256件、native bufferの既定値は8192 bytesとする。通知上限は正数、native bufferは4096～65536 bytesを要求する。共通HotReloadが持つ保留ID数の上限とは別であり、各段階で独立して容量を制限する。

OS側の監視はroot全体を再帰的に対象とする。adapterのqueueと再構築は追跡中入力に限定できるが、関係のないnativeイベント自体が消えるわけではない。対象照合は現在の集合を走査し、RescanのRevision確認も追跡入力数に依存する。ファイルごとのwatcher数を増やさない代わりに、大規模な入力集合では通知頻度と照合・再走査コストの測定が必要になる。

## 公開APIと契約

比較元は `2b5e6ac`。FileSystem Sourceに追加・変更するAPIを示す。`IResourceChangeSource` と `IResourceWatchTargets` は共通HotReloadの既存契約を使う。

```diff
 namespace Lumyte.Resources.FileSystem
 {
     public sealed class FileResourceSource : IResourceSource
     {
-        public FileResourceSource(FileResourceSourceOptions options);
+        // changeOptions省略時は既定の容量を使う。ここではwatcherを起動しない。
+        public FileResourceSource(FileResourceSourceOptions options, FileResourceChangeSourceOptions? changeOptions = null);
+        // 読み込みと同じroot。各列挙が独立した購読を所有する。
+        public IResourceChangeSource Changes { get; }
     }
+    public sealed record FileResourceChangeSourceOptions
+    {
+        // 正数。超過時は個別通知を一つのRescanに置き換える。
+        public int NotificationCapacity { get; init; } = 256;
+        // 4096～65536 bytes。OS通知欠落時もRescanを要求する。
+        public int InternalBufferSizeBytes { get; init; } = 8192;
+    }
 }
```

## 検討した代替案

- 独立した監視元をDIへ登録する: 読み込み元と違うroot・ID体系を組み合わせられるため、Sourceの `Changes` に統合する。
- root内の全イベントをManagerへ渡す: 未ロード入力がqueueとRevision確認を消費するため、adapterで最新対象を絞り込む。
- ファイルごとにwatcherを作る: 対象選別は簡単になるが、多数の入力でOSリソースと購読更新が増える。root単位の再帰watcherを採用する。
- callbackから直接再構築する: 通知threadを長く占有し、重複通知・実行スレッド制約・公開競合を扱えない。軽いhintと共通HotReloadへ分離する。
- 定期的に全ファイルを走査する: native通知には依存しないが、未ロード入力にも継続的なメタデータI/Oが発生する。通常はイベントで待機し、通知欠落時だけ追跡入力を再確認する。

## 結果と影響

Sourceを登録するだけで、その入力の編集を自動HotReloadへ接続できる。通知欠落を再走査へまとめることでqueue容量を制限できるが、編集から公開までの厳密な期限や全イベントの配送は保証しない。rootの寿命、native watcherの提供される環境、終端エラー後の構成再作成は利用側が扱う。

## 検証方針

`FileResourceChangeSourceTests` でFileResourceSourceだけを渡したManagerの自動更新、旧Lease保持、初期Rescan、編集・rename・atomic replacement・directory削除を実ファイルで確認する。未追跡入力の除外と対象追加、通知queue overflowのRescanへの変換、購読ごとの取消と解放、無関係なlinkが購読を停止しないことを検証する。

直接読み込みが書き込み通知を発生させないこと、設定範囲とlinked rootの拒否も確認する。Manager側の対象来歴、監視無効化、終了順、通知失敗の観測は共通HotReloadのテストと組み合わせる。OSごとの通知順序には依存せず、編集後のRevision確認と公開結果を受け入れ条件とする。
