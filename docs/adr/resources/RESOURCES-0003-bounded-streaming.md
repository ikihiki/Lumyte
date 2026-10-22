# ADR-RESOURCES-0003: 独立sessionと固定容量のストリーミング

- 状態: 提案
- 日付: 2026-10-20

## 背景

長いデータを全量常駐させず、複数の利用者が独立した位置から読みたい。
音声consumerなどの定常経路でI/O、デコード、lock、割り当てを避けたい。

## 決定

Pipeline出力は不変factoryとし、sessionごとにreader、cursor、memory scope、factory leaseを所有する。
固定個数・固定長bufferで先読みし、満杯でproducerを止める。
非同期ReadAsyncは準備を待ち、ReadAvailableは準備済み要素だけを返す。
0をunderflowと終端に区別するためEndOfStreamを別に公開する。
consumerは一つとし、readとseekの並行利用は許可しない。
seekは先読みを終了してからreaderを移動する。失敗したsessionはfaultになる。
producer、reader、buffer/scope、factory leaseの順で終了し、失敗した解放は再試行する。
旧sessionは再読み込み後も旧factoryを保持する。これは入力ファイルの旧版保存を意味しない。
直接読み込みのSourceで変更を検出した場合、既存readerの読み込みは失敗しsessionはfaultになる。
利用側は新しいfactoryからsessionを開き直す。実行時のファイル版管理は対象外とする。
初期実装はproducer再開・非同期待機に1 ms pollを使う。codecとAudio出力接続は対象外。

## APIと契約

比較元はこのPRの依存ブランチ。以下は判断対象の追加・変更APIの抜粋。

```diff
+namespace Lumyte.Resources
+{
+    public interface IResourceStream<T> where T : unmanaged
+    {
+        ValueTask<IResourceStreamReader<T>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default);
+    }
+    public sealed class ResourceManager
+    {
+        public ValueTask<ResourceStreamSession<TElement>> OpenStreamAsync<TResource, TElement>(ResourceKey<TResource> key, ResourceStreamOptions options, CancellationToken cancellationToken = default) where TResource : class, IResourceStream<TElement> where TElement : unmanaged;
+    }
+    public interface IResourceStreamReader<T> : IAsyncDisposable where T : unmanaged
+    {
+        bool CanSeek { get; }
+        ValueTask<int> ReadAsync(Memory<T> destination, CancellationToken cancellationToken = default);
+        ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default);
+    }
+    public sealed record ResourceStreamOptions
+    {
+        public int BlockSize { get; init; } = 4096;
+        public int MaxBufferedBlocks { get; init; } = 2;
+        public MemoryPoolId BufferPool { get; init; }
+    }
+    public sealed class ResourceStreamSession<T> : IAsyncDisposable where T : unmanaged
+    {
+        public ulong Revision { get; }
+        public bool EndOfStream { get; }
+        public int ReadAvailable(Span<T> destination);
+        public ValueTask<int> ReadAsync(Memory<T> destination, CancellationToken cancellationToken = default);
+        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default);
+        public ValueTask DisposeAsync();
+    }
+}
```

## 検討した代替案

全量デコードは大きな常駐領域を必要とする。readerを共有するとconsumer間でcursorが干渉する。
consumerから直接I/Oすると定常処理の待ち時間を制御できない。

## 結果と影響

メモリ上限と独立位置を保証できるが、pollによる待機遅延とworkerの負担がある。
codec内部の作業領域はdecoderがsession相当のscopeへ計上する必要がある。

## 検証方針

短いread、終端、underflow、満杯時の先読み停止、seek、独立cursor、
再読み込み時の旧session維持、reader解放失敗の再試行とcharge返却を検証する。

## 関連ADR

- [ADR-RESOURCES-0001](RESOURCES-0001-typed-pipelines.md)
- [ADR-RESOURCES-0002](RESOURCES-0002-generations-reload.md)
