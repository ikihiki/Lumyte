# ADR-MEMORY-0001: DIで交換できるメモリ確保・予算・使用量計上の契約

- 状態: 提案
- 日付: 2026-10-10
- 関連カテゴリ: core、resources、graphics

## 背景

リソースのCPUデータ、GPU生成物、入力snapshot、ストリームbufferを同じ予算の中で扱いたい。HotReloadやデバイス復旧では旧値と候補値が同時に存在し、共有するdocumentを各meshから重複計上しても、未解放のGPU資源を計上から消しても正しく判断できない。

確保アルゴリズムや排除方針を固定する前に、リソースシステムが使える確保、予約、計上、解放通知の境界を定める。本ADRはインターフェースの提案であり、アロケーターの実装、物理メモリ量の厳密な測定、LRUなどの選定は行わない。

## 決定

### 配置とDI

契約を `src/Core/Lumyte.Memory/`、名前空間・パッケージ名を `Lumyte.Memory` に置く。標準DIへの登録は `Lumyte.Memory.DependencyInjection` が担当する。MemoryはResources、Graphics、Audioに依存しない。利用側はIMemoryManagerを注入し、managerの実装をDIで交換する。

IMemoryManagerは同じ予算を共有するEngineのDI rootでsingletonとする。world、リソース世代、入力snapshot、再生セッションは短いIMemoryScopeを作る。scopeは利用側が所有する実行時オブジェクトであり、DI scopeとは別である。全scopeとキャッシュ登録を終了してからDI rootを終了する。

初期実装では標準poolと単純な予約・計上表を使ってよい。実装を交換しても、予約の二重消費防止、解放の冪等性、未使用候補だけの回収、失敗した解放の計上保持は変えてはならない。

### 予算と計上単位

MemoryPoolIdは `cpu`、`gpu/main` などの予算bucketを識別する。GPUのepochはOwnerまたは確保Labelの診断情報へ付け、同じ描画contextの旧epochと新epochを同じbucketで合算する。デバイスロストやReload開始だけでは使用量をリセットしない。diskに保持したsnapshotの容量はRAM予算とは分け、RAMへ展開した部分を計上する。

SoftLimitは回収を検討する閾値、HardLimitは新規予約を認める上限とする。`CommittedBytes + ReservedBytes + requestedBytes <= HardLimitBytes` を同期して検査し、複数の同時予約でも同じ余力を使わない。値は非負、SoftLimitはHardLimit以下とし、計算のoverflowを拒否する。未設定のbucketは無制限として明示的に扱う。

予算は追跡対象の使用量を制御するもので、GC heap、pool内に返却済みの領域、driver内部、OSのworking set全体を制限する保証ではない。GPUの確保量が取得できない場合はEstimateとして計上し、Exactと混ぜて正確な物理量であるかのように表示しない。

同じ物理bufferやdocumentを共有する場合、所有するscopeで一回だけ計上する。依存する各出力は利用権を持つが、バイト数を重ねて計上しない。CPU入力、GPU出力、stagingは別の確保なのでそれぞれ計上する。streamも共有factoryのheaderとセッションごとのbuffer・decoder領域を分ける。

### 予約、確保、解放

外部のGPU／Native確保は予約してから行い、成功した確保をCommitでchargeへ変換する。予約とchargeを同時に二重計上しない。確保しなかった予約は返却する。Commitは一度だけ可能で、返却後や二重CommitはInvalidOperationExceptionとする。

実際の計上値が予約を超えた場合も、既に存在する確保を台帳から消してはならない。Commitは実際値へ更新してchargeを返し、予算超過をsnapshotへ記録する。利用側はその候補を公開せず、確保を片付ける。超過中は追加予約を拒否する。解放に失敗したらchargeを残す。これにより予測誤差による超過は観測できるが、事前予約だけで未知の物理確保量を保証するものではない。

CPUのRentAsyncは確保と計上を一つの所有bufferへ包む。Memoryの長さは要求された要素数、Capacityは実際に借りた要素数とし、計上にはCapacityを使う。実Capacityが予約を超える場合も公開前に予算を確認し、HardLimitを超えるbufferを成功結果として返さない。片付けに失敗した確保はscopeへ計上したまま残す。管理用のheaderなど計測できない部分は追跡対象の定義から区別する。buffer返却は基盤がcharge返却まで担当し、利用側が別chargeを作ってはならない。

IMemoryScopeは生成したbuffer、予約、chargeを保持する。bufferの早期返却もscope終了時の返却も冪等に処理する。外部確保のchargeには物理解放処理を含めないため、scopeを閉じる側は先に対応する外部確保を解放する。失敗した物理解放のchargeをscope終了で先に消してはならない。確保・解放を行う機能と計上基盤の責任をこの順序で接続する。

### 回収の境界

IMemoryReclaimerを登録し、明示的なReclaimAsyncから回収を依頼する。予約処理そのものはreclaimerを呼ばず、余力不足ならMemoryBudgetExceededExceptionを返す。利用側は失敗した候補を片付けてから回収・再要求を判断でき、構築中の予約が同じmanagerのTrimを再入的に呼ぶことを避ける。

回収は予算lockの外で行い、同じbucketの回収要求を直列化する。回収中に同じbucketへ新規予約して空きを消費するようなreclaimerは禁止する。実装は未使用cacheからだけ回収し、lease、構築依存、read scope、再生セッションなどの利用権を強制的に返却しない。返すバイト数はそのbucketで実際に返却できたchargeの合計であり、親の推定sizeや目標値ではない。

目標量を回収できなくても待ち続けず、実際の量を返す。回収後の予約は改めて同期して判定する。callback失敗は他の独立した候補を処理した後に通知し、解放できなかった領域は計上を維持する。

### 公開API案

比較元は `main` の `4b8902f5226c17ce86c337d09434a3ae4043bf2d`。Memoryパッケージは存在しないため、すべて追加として示す。宣言は主要メンバーの抜粋である。

```diff
+namespace Lumyte.Memory
+{
+    public readonly record struct MemoryPoolId
+    {
+        // 非空、ordinalで比較。defaultはAPI境界で拒否する。
+        public MemoryPoolId(string value);
+        public string? Value { get; }
+    }
+    public enum MemoryPurpose { Build, Resident, Streaming, SourceSnapshot }
+    public enum MemoryAccuracy { Exact, Estimate }
+    public readonly record struct MemoryUsage(ulong Bytes, MemoryAccuracy Accuracy);
+    public sealed record MemoryRequest
+    {
+        public required MemoryPoolId Pool { get; init; }
+        // 正数。予約する上限または保守的な見積もり。
+        public required ulong Bytes { get; init; }
+        public required MemoryPurpose Purpose { get; init; }
+        // 確保の診断情報。device epochなどを記録し、予算bucketは分けない。
+        public string? Label { get; init; }
+    }
+    public sealed record MemorySnapshot
+    {
+        public required ulong CommittedBytes { get; init; }
+        public required ulong ReservedBytes { get; init; }
+        public required ulong EstimatedBytes { get; init; }
+        public required ulong SoftLimitBytes { get; init; }
+        public required ulong HardLimitBytes { get; init; }
+        public required bool IsOverBudget { get; init; }
+    }
+    public interface IMemoryReservation : IDisposable
+    {
+        public MemoryRequest Request { get; }
+        // 予約からchargeへ一回だけ移す。scopeもchargeを保持する。
+        // 予約を超える計上でも責任を失わず、超過をsnapshotへ反映する。
+        IMemoryCharge Commit(MemoryUsage usage);
+        // 未Commitの予約を返す。Commit後はchargeを解放しない。冪等。
+        new void Dispose();
+    }
+    public interface IMemoryCharge : IDisposable
+    {
+        public MemoryPoolId Pool { get; }
+        public MemoryUsage Usage { get; }
+        // 対応する確保を実際に解放した後だけ呼ぶ。物理資源は解放しない。冪等。
+        new void Dispose();
+    }
+    public interface IMemoryBuffer<T> : System.Buffers.IMemoryOwner<T> where T : unmanaged
+    {
+        public int Capacity { get; }
+        public ulong ChargedBytes { get; }
+        // Memoryは要求長。返却後のアクセスはObjectDisposedException。
+        // Disposeはbufferとchargeを一緒に返す。scopeも二重返却しない。
+    }
+    public interface IMemoryScope : IAsyncDisposable
+    {
+        public string Owner { get; }
+        // 予約だけを行う。budget不足はMemoryBudgetExceededException。
+        ValueTask<IMemoryReservation> ReserveAsync(MemoryRequest request, CancellationToken cancellationToken = default);
+        // CPUのtyped memory。正のlength。capacityのbyte数もoverflowを検証する。
+        ValueTask<IMemoryBuffer<T>> RentAsync<T>(MemoryPoolId pool, int length, MemoryPurpose purpose, CancellationToken cancellationToken = default) where T : unmanaged;
+        // 外部確保は先に解放する。途中失敗なら残りを保持して再試行できる。
+        ValueTask DisposeAsync();
+    }
+    public interface IMemoryReclaimer
+    {
+        // 生存中の利用権を壊さず、実際に返した当該poolのbyte数を返す。
+        ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default);
+    }
+    public interface IMemoryManager : IAsyncDisposable
+    {
+        // ownerは診断用。実行時scopeの所有権を呼び出し側へ渡す。
+        IMemoryScope CreateScope(string owner);
+        MemorySnapshot GetSnapshot(MemoryPoolId pool);
+        // 登録解除tokenはcache所有者が保持する。reclaimer自体は借用。
+        IDisposable RegisterReclaimer(IMemoryReclaimer reclaimer);
+        // 目標未達でも実際の解放byte数を返す。予約は自動再試行しない。
+        ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default);
+        // scope・charge・予約・登録が残る場合は状態を変えず拒否する。
+        ValueTask DisposeAsync();
+    }
+    public sealed class MemoryBudgetExceededException : InvalidOperationException
+    {
+        public MemoryPoolId Pool { get; }
+        public ulong RequestedBytes { get; }
+        public MemorySnapshot Snapshot { get; }
+    }
+}
+namespace Lumyte.Memory.DependencyInjection
+{
+    public sealed class MemoryManagementOptions
+    {
+        // 構成時のみ。0<=soft<=hardを検証し、DI構築後は変更しない。
+        public void SetBudget(MemoryPoolId pool, ulong softLimitBytes, ulong hardLimitBytes);
+    }
+    public static class MemoryServiceCollectionExtensions
+    {
+        // singleton登録。初期版は標準poolと計上表でよい。重複構成は拒否する。
+        public static IServiceCollection AddMemoryManagement(this IServiceCollection services, Action<MemoryManagementOptions>? configure = null);
+        // custom実装へ同じ契約で差し替える。自動実装のアルゴリズムを要求しない。
+        public static IServiceCollection AddMemoryManagement<TManager>(this IServiceCollection services)
+            where TManager : class, IMemoryManager;
+    }
+}
```

MemorySnapshotのEstimatedBytesはCommittedBytesに含まれる見積もり分であり、加算する別使用量ではない。予約は別欄に表示する。ReserveAsyncのキャンセルは予約確定前なら何も消費せず、確定後は予約tokenを成功結果として渡し、利用側が返却する。RentAsyncが失敗した場合はbufferと予約を片付け、未返却があればscopeの責任として残す。

### リソースシステムへの接続

[RESOURCES-0001](../resources/RESOURCES-0001-resource-management.md) のcontextがBuild用・Resident用のscopeを提供する。ステージがRent／Reserveした領域をmanagerが追跡し、Build完了時または出力・補助資源の解放成功後にscopeを閉じる。SourceはDIで同じIMemoryManagerを受け取り、snapshot単位のscopeを所有する。stream readerにはセッションのscopeを貸し、readerの終了成功後にmanagerがbufferと計上を返す。

ResourceManagerは未使用cacheを回収するreclaimerを登録し、終了時に解除する。CPU・GPUの共有依存を解放した結果の実際のcharge返却量で応答する。HotReload候補も構築中から予約・計上し、メモリ不足なら旧公開値を壊さず候補を失敗させる。device lost後の未解放の確保も同様に保持する。

## 検討した代替案

- リソース型ごとにsizeを報告するだけ: 一つの共有documentの重複計上や、構築中のstagingの見落としが起きる。実際の確保の所有にchargeを結び付ける。
- メモリmanagerがGPU／Nativeの確保・解放まで実行する: deviceと形式への依存が共通層へ入る。予算・台帳と外部確保を分離する。
- 余力不足の予約から自動的に同じmanagerのTrimを呼ぶ: 構築や解放処理への再入が起きる。回収は明示的な境界で行う。
- 最初から高度なpool・LRUを必須にする: 利用側の寿命契約よりアルゴリズムの選定が先行する。初期版は単純にし、インターフェースから交換できるようにする。

## 結果と影響

- 構築中、共有中、失敗した解放、旧GPU epochを同じ台帳へ残せる。
- リソースシステムから独立した実装をDIで選べる。ステージは追跡外の確保も予約とchargeへ接続する必要がある。
- 予算不足は回収や再試行を利用側へ伝える。生存中のleaseを捨てて強制的に成功させない。
- 見積もりやpool内の保持領域があるため、管理対象の台帳と実際のprocess・driverの物理使用量は区別する。

## 検証方針

同時予約、予約のCommit／返却、実際値の超過、確保失敗、二重Dispose、scope終了の再試行を検証する。共有documentの一回計上、CPU／GPU／stagingの分離、streamのセッション別計上、Reload候補と旧値の合算、device lost後の計上保持も確認する。reclaimerが生存中の利用権を壊さず、独立した候補の失敗でも台帳から消さないことを確認する。

## 別途決定する事項

- allocator、pool、Native alignment、GPU確保量の問い合わせと見積もり式。
- LRU、優先度、圧力通知の配送、runtime予算変更、回収の頻度。
- pool内部の保持領域やdriver・OSメモリを含む計測、診断表示。
