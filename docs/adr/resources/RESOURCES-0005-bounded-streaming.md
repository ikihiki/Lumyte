# ADR-RESOURCES-0005: 独立sessionと固定容量のストリーミング

- 状態: 提案
- 日付: 2026-10-20

## 背景

長い音声や連続データを全量常駐させると、入力サイズに応じてメモリ使用量が増える。同じ入力を複数箇所から利用する場合も、readerやcursorを共有すると再生位置が干渉する。定常的にデータを消費する側では、I/O・デコードの遅延やメモリ確保を避け、準備済みの要素だけを受け取りたい。

[共有Pipeline](RESOURCES-0001-typed-pipelines.md) の出力と [内容世代](RESOURCES-0002-generations-reload.md) の保持を利用し、共有する不変factoryと、利用者ごとに変わる読み込み状態を分ける。

## 決定

### factoryとsessionの責務

`Lumyte.Resources` に `IResourceStream<TElement>` と `ResourceStreamSession<TElement>` を置く。Pipelineの出力は不変のfactoryとし、codecや入力形式を共通層へ固定しない。factoryは呼び出しごとに独立したreaderを作り、readerがcursorとdecoder状態を持つ。`TElement` はunmanaged型とし、seek位置は圧縮byte位置ではなく要素単位で表す。

`ResourceManager.OpenStreamAsync` はfactoryの取得、内容世代のLease、session専用memory scope、固定数のbuffer、readerを順に用意する。最初のblockまたは終端を確認してからsessionを返す。以後factoryのRevisionは変わらず、HotReloadによる公開変更は新しく開くsessionへ反映される。

sessionはfactoryの内容世代を保持するが、外部ファイルの旧内容を保存しない。直接読み込みを行うreaderが入力変更を検出すれば、旧sessionも失敗する。再開には新しいfactoryからsessionを開き直す。

### 読み込みと容量

固定長・固定個数のblockを単一producerと単一consumerで共有する。producerは空blockだけをreaderへ渡し、正の要素数を公開してから次へ進む。consumerは公開済み要素をコピーし、block全体の消費後に空へ戻す。readerの短いreadは許可し、0を終端とする。負数または渡した領域を超える要素数はreaderの契約違反としてsessionを失敗させる。

`ReadAvailable` は準備済み要素をコピーし、I/O・デコード・待機を行わない。blockを空にすると、待機中のproducerへ再利用するwork itemで通知する。readerとproducerの継続をconsumer上でinline実行せず、通知ごとのTaskやdelegateも生成しない。戻り値0はunderflowにも終端にもなり得る。`EndOfStream` はproducerが終端へ到達し、準備済み要素を消費し終えたときだけtrueになる。`ReadAsync` は準備を待ち、空のdestinationまたは終端の場合だけ0を返す。

同じsessionのRead・Seek・Disposeを重ねて呼んではならない。異なるsessionは独立して利用できる。`DisposeAsync` 同士の並行呼び出しだけは同じ解放試行を共有する。consumerを常駐スレッドへ固定する契約は設けないが、呼び出しの直列化は利用側の責任とする。

### seek・取消・失敗

`SeekAsync` はproducerの取消と実際の完了を待ってからreaderの位置を変更する。成功後に古いbufferを捨て、Positionを指定位置へ置き換えて先読みを再開し、新位置の最初のblockまたは終端まで待つ。readerがseek非対応なら `NotSupportedException` を返し、現在のsessionを維持する。操作開始前の取消も位置を変更しない。

seek開始後の失敗・取消はsessionをfault状態にし、producerを停止する。producerのI/Oやデコードの例外も保存し、Read・Seekで再送出する。途中まで取得したbufferを成功値として流し続けない。faultになってもreader、buffer、Leaseの解放責任は残る。

生成時のtokenはsession生成だけを取り消し、生成後のproducer寿命には接続しない。`ReadAsync` のtokenはその待機だけを取り消し、先読みを止めない。producerの停止はSeekまたはDisposeが担当する。readerが取消に応じない場合、Seek・Disposeの完了もそのreader処理を待つ。

### 所有権と終了

sessionはproducer、reader、buffer、memory scope、factory Leaseを所有する。readerへ渡すmemory scopeは借用であり、reader自身はscopeを破棄しない。codecが別の作業領域を必要とする場合も、このscopeへ計上する。

終了はproducer停止 → reader解放 → buffer返却 → scope解放 → Lease返却の順とする。途中の解放が失敗した場合はそれ以降を保持し、次の `DisposeAsync` で未完了部分を再試行する。破棄開始後はRead・Seekを拒否する。利用側は全sessionを返してからManagerを終了する。

生成に失敗した場合も同じ順で後始末する。生成エラーと解放エラーが重なった場合は両方を `AggregateException` で返し、利用者に渡せなかったsessionの所有はManagerが保持する。`TrimAsync` がその解放を再試行する。

### 性能と制約

要求するbuffer要素数は `BlockSize × MaxBufferedBlocks` に固定し、入力の長さに応じて増やさない。実際のメモリ計上はpoolの確保単位に従い、codec作業領域とsessionの管理オブジェクトは別に必要となる。buffer数・長さは正数とし、積のoverflowを生成前に拒否する。

producerは満杯時に再利用する通知を待ち、timerを繰り返し生成しない。通知を登録してから空きと取消を再確認することで、登録直前の消費でも再開を失わない。Seek・Disposeも通知で待機を解除し、producer完了後に状態を変更する。通知はThreadPoolへqueueするため、専用threadやbusy spinは不要だが、runtime内部の初期化・queue拡張を含む厳密な無割り当てや復帰期限は保証しない。

`ReadAsync` のunderflow待機には `Task.Delay(1)` を使う。この便利APIはtimer・schedulerの負担と割り当てを伴い、1 msの復帰期限も保証しない。デバイスcallbackは待機しない `ReadAvailable` を使う。buffer量、underflow時の無音補填、供給周期は利用するcodec・デバイス側が決める。

## 公開APIと契約

比較元は `d701120`。追加する判断対象APIを示す。

```diff
+namespace Lumyte.Resources
+{
+    public interface IResourceStream<TElement> where TElement : unmanaged
+    {
+        // 独立cursorを返す。memoryは借用で、readerはscope自体を破棄しない。
+        ValueTask<IResourceStreamReader<TElement>> OpenReaderAsync(ResourceStreamOptions options, IMemoryScope memory, CancellationToken cancellationToken = default);
+    }
+    public interface IResourceStreamReader<TElement> : IAsyncDisposable where TElement : unmanaged
+    {
+        bool CanSeek { get; }
+        // 短いreadを許可する。0は終端。返す要素数はdestination.Length以下。
+        ValueTask<int> ReadAsync(Memory<TElement> destination, CancellationToken cancellationToken = default);
+        // 絶対要素位置。圧縮byte位置ではない。
+        ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default);
+    }
+    public sealed record ResourceStreamOptions
+    {
+        // 各blockの要素数とblock数。ともに正数。
+        public int BlockSize { get; init; } = 4096;
+        public int MaxBufferedBlocks { get; init; } = 2;
+        public MemoryPoolId BufferPool { get; init; } = new("cpu");
+    }
+    public sealed class ResourceManager
+    {
+        // 最初のblockまたは終端まで準備する。tokenは生成だけを取り消す。
+        public ValueTask<ResourceStreamSession<TElement>> OpenStreamAsync<TResource, TElement>(ResourceKey<TResource> key, ResourceStreamOptions options, CancellationToken cancellationToken = default)
+            where TResource : class, IResourceStream<TElement> where TElement : unmanaged;
+    }
+    public sealed class ResourceStreamSession<TElement> : IAsyncDisposable where TElement : unmanaged
+    {
+        public ulong Revision { get; }
+        // 消費済み要素数。seek成功時に指定位置へ変わる。
+        public ulong Position { get; }
+        public bool CanSeek { get; }
+        public bool EndOfStream { get; }
+        // 準備済み分だけをコピーする。0だけで終端と判断しない。
+        public int ReadAvailable(Span<TElement> destination);
+        // tokenはこの待機だけを取り消す。producerは継続する。
+        public ValueTask<int> ReadAsync(Memory<TElement> destination, CancellationToken cancellationToken = default);
+        // 開始後の失敗・取消はsessionをfaultにする。
+        public ValueTask SeekAsync(ulong elementOffset, CancellationToken cancellationToken = default);
+        // 並行呼び出しは一つの試行を共有し、解放失敗は次回呼び出しで再試行できる。
+        public ValueTask DisposeAsync();
+    }
+}
```

## 検討した代替案

- 全量デコードして共有する: cursorは単純になるが、入力の長さに比例する常駐領域を必要とする。
- readerを共有する: 同時利用者のseekと先読みが干渉し、所有と同期が複雑になる。
- consumerから直接I/O・デコードする: bufferは減るが、定常処理へ入力待ちと割り当てが入り込む。
- producerが満杯の間もpollする: consumerからの通知は省けるが、停止したconsumerを待つだけでtimerとTaskを生成し続ける。通知を再利用し、readerの継続をconsumerから切り離す方式を採用する。

## 結果と影響

入力サイズに依存しないbuffer量と独立cursorを提供できる。factory世代の保持によりHotReloadと並行して既存sessionを使えるが、入力自体の不変性はSourceの契約に依存する。取消に応じるreader、codec作業領域の計上、underflowへの対応は利用側が実装する必要がある。

## 検証方針

`ResourceStreamTests` で独立cursor、短いread、空入力、終端、underflow、満杯時の先読み停止、長い入力での固定buffer量を確認する。準備済みの `ReadAvailable` とblock解放の通知経路をwarmup後に測定し、consumer側の割り当てがないことを確認する。readerをinline実行しないこと、単一blockの反復で通知を失わないこと、満杯のまま終了できることも検証する。

seekでは停止中producerの完了待ち、古い先読みの破棄、非対応時の継続、失敗後のfaultを確認する。生成・readの取消範囲、HotReload前後のsession世代、並行Dispose、reader解放失敗時のbuffer・Lease保持と再試行、生成失敗後のManagerによる後始末を検証する。timerの実際の復帰遅延やデバイスの供給期限は別途実環境で測定する。
