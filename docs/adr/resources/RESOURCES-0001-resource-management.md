# ADR-RESOURCES-0001: 型付きリソースの非同期読み込みと共有寿命管理

- 状態: 提案
- 日付: 2026-10-09
- 関連カテゴリ: core、graphics、platform

## 背景

ゲーム内の画像、音声、モデルなどを各利用者が直接読み込むと、同じデータの重複読み込みと、共有中のオブジェクトの早期解放が起きる。読み込み元やデコード方式を交換できる共通基盤で、識別、読み込み、共有、解放を管理する必要がある。

本提案では「リソース」をゲームで利用するアセットの実行時オブジェクトとして扱う。CPU／GPUメモリの汎用アロケーター、OSハンドル管理、開発用アセットデータベースは対象にしない。この対象範囲とAPIはレビューで合意する設計案であり、実装済みの契約ではない。

## 決定

### 範囲と依存関係

`src/Core/Lumyte.Resources/` に `Lumyte.Resources` パッケージを追加する方針とする。配置は [ADR-0002](../0002-repository-layout.md) に従う。本PRではプロジェクトを作成せず、設計だけを追加する。

共通基盤は識別子、型別ローダー登録、非同期読み込みの集約、利用権、キャッシュを担当し、Graphics、Audio、Platform、DIコンテナーに依存しない。媒体からの取得は `IResourceSource`、バイト列からの実行時オブジェクト生成と解放は `IResourceLoader<T>` が担当する。ファイル、HTTP、Browser向けの取得元は別アダプター、画像やモデルのローダーは対応する機能側に置く。Engineまたはアプリの構成側が取得元とローダーを選択し、managerを所有する。

初期範囲はオンデマンド読み込み、型単位の共有、明示的な未使用キャッシュ解放とする。自動LRU、予算管理、プリロード専用API、ホットリロード、アセット変換・パック形式、ローダーからの依存アセット取得は別途設計する。managerをグローバルsingletonにせず、シーン単位などで独立して構成できる。

### 識別子と共有単位

`ResourceId` は取得元内の論理名であり、OSの絶対パスやURLを直接表さない。非空の `/` 区切り相対名とし、先頭・末尾の `/`、空セグメント、`.`、`..`、バックスラッシュ、制御文字、`:`、`?`、`#` を拒否する。大文字小文字を区別するordinal比較を使用し、暗黙の小文字化、URLデコード、Unicode正規化を行わない。`default(ResourceId)` は無効とし、API境界でも検証する。

キャッシュキーはmanager内の `(ResourceId, typeof(T))` とする。同じ名前でも型が違えば別エントリーであり、異なるmanager間では共有しない。一つのmanagerは取得元を一つ借用し、型ごとにローダーを一つ登録する。取得元と登録は構築後に変更できない。デコードオプションやGPU deviceが異なる場合はmanagerを分ける。バージョン・派生形式を区別する必要がある場合は別の論理名にする。

### 公開API案

比較元は `main` の `f3f4f670d48ffbd9f663f9ec737e462a280cd5ca`。比較元に `Lumyte.Resources` は存在しないため、すべて追加として示す。宣言は主要メンバーの抜粋である。

```diff
+namespace Lumyte.Resources
+{
+    public readonly record struct ResourceId
+    {
+        // 上記の論理名規則を検証。不正値はArgumentException。
+        public ResourceId(string value);
+        // 検証済みの論理名。defaultインスタンスではnull。
+        public string? Value { get; }
+    }
+
+    public interface IResourceSource
+    {
+        // 読み取り可能なstreamの所有権を呼び出し側へ渡す。
+        // seek可能性・Lengthは保証しない。未存在はFileNotFoundException。
+        ValueTask<Stream> OpenReadAsync(ResourceId id, CancellationToken cancellationToken = default);
+    }
+
+    public interface IResourceLoader<T> where T : class
+    {
+        // streamは借用。戻り値は非nullで、managerへ排他的な解放責任を渡す。
+        // 失敗・キャンセル時の部分生成物はローダーが解放する。
+        ValueTask<T> LoadAsync(ResourceId id, Stream source, CancellationToken cancellationToken = default);
+        // 成功した生成物の解放。IDisposableの有無にかかわらず必ずこの経路を使う。
+        // スレッド制約やGPU使用終了の確認もローダーの責務。
+        ValueTask UnloadAsync(T resource);
+    }
+
+    public sealed class ResourceManagerBuilder
+    {
+        // sourceは借用。nullはArgumentNullException。
+        public ResourceManagerBuilder(IResourceSource source);
+        // 型ごとに一つ登録。重複はInvalidOperationException。loaderは借用。
+        public ResourceManagerBuilder AddLoader<T>(IResourceLoader<T> loader) where T : class;
+        // 登録のsnapshotから独立したmanagerを生成する。
+        public ResourceManager Build();
+    }
+
+    public sealed class ResourceManager : IAsyncDisposable
+    {
+        // 同じキーの読み込みを一回にまとめ、呼び出しごとに独立したleaseを返す。
+        // 未登録型はNotSupportedException。解放済みmanagerはObjectDisposedException。
+        public ValueTask<ResourceLease<T>> AcquireAsync<T>(ResourceId id, CancellationToken cancellationToken = default) where T : class;
+        // 利用権がゼロのReadyエントリーを解放。取得・解放との競合を同期する。
+        // 呼び出し開始時の候補だけを処理し、成功したエントリー数を返す。
+        public ValueTask<int> TrimAsync(CancellationToken cancellationToken = default);
+        // leaseまたは読み込みが残る場合は状態を変えずInvalidOperationException。
+        // 未使用エントリーを解放して終了。成功後の再呼び出しは何もしない。
+        public ValueTask DisposeAsync();
+    }
+
+    public sealed class ResourceLease<T> : IDisposable where T : class
+    {
+        // このleaseが有効な間だけ借用できる。解放後はObjectDisposedException。
+        public T Value { get; }
+        // 利用権だけを返却。I/O、生成物の解放、GPU待機を行わない。冪等。
+        public void Dispose();
+    }
+}
```

### 読み込みとキャンセル

エントリーは `Loading → Ready → Unloading → Removed` と遷移する。同じキーに同時に要求が来た場合は一つの読み込みtaskを待つ。取得元のstreamを開き、ローダーを呼び、streamの解放まで成功してからReadyとして公開する。streamの解放が失敗した場合も、生成済みオブジェクトをローダー経由で片付け、利用者には公開しない。

Acquireのキャンセルはその呼び出しの待機だけを終了し、他の待機者や共有読み込みをキャンセルしない。読み込みは待機者がゼロでも完了まで継続し、成功した値は未使用キャッシュに残す。内部読み込みには呼び出し側のtokenを渡さず、初期契約では `CancellationToken.None` を渡す。無期限に停止した取得元を中断する仕組みは本提案に含めず、取得元側のタイムアウトで制限する。

lease発行とキャンセル判定を同じ同期境界で確定する。発行前にキャンセルを観測した要求にはleaseを作らない。発行確定後は成功を返し、tokenの後続キャンセルで利用権を失わせない。失敗した読み込みは現在の待機者全員へ通知し、部分生成物の片付け後にエントリーを取り除く。次のAcquireで再試行でき、失敗結果を恒久キャッシュしない。

### 所有権とキャッシュ解放

managerが生成物を所有し、leaseは利用権を一つ持つ。`T` は共有されるため、利用側は直接Disposeせず、破壊的変更には独自の同期を用いる。取得したValueをleaseより長く保持して使用してはならない。lease自体をコピーしても利用権は増えず、追加の利用者はAcquireで別leaseを取得する。finalizerによる解放は保証しない。

最後のlease返却では即時解放せず、未使用キャッシュとして保持する。Trimまたはmanager終了時にローダーのUnloadを呼ぶ。TrimがエントリーをUnloadingに移す時点で利用権ゼロを確認し、その後の同じキーのAcquireは解放完了を待って新規読み込みする。解放と新規lease発行を同時に行わない。

Unload失敗は `CleanupFailed` として値と解放責任を保持し、Acquireでその値を再公開しない。後続のTrimまたはDisposeで再試行する。ローダーは部分的な解放後の再呼び出しを安全に処理し、成功済み解放を二重実行しない。読み込み失敗後の片付けも失敗した場合は同じ状態で保持し、元の失敗と片付け失敗を集約して通知する。

Trimは候補を順に解放し、個別の失敗があっても他の候補を処理し、最後にAggregateExceptionを返す。キャンセルは次の候補を開始する前に観測し、開始済みUnloadは途中で中断しない。成功済みの解放は取り消さない。

Disposeは利用権、読み込み、進行中のTrimが残る場合に拒否する。終了を開始した後は新規AcquireとTrimを拒否し、全エントリーを解放する。失敗時は終了中のまま責任を保持し、Disposeの再呼び出しだけを許す。同時のDisposeは同じ終了処理を待つ。取得元とローダーはmanagerが破棄せず、構成側がすべてのmanager終了後に解放する。

### スレッド、GPU、プラットフォーム

Acquire、lease返却、Trimの競合はmanager内部で同期する。ユーザーコードやI/Oをlock内で呼ばない。Builderは構成時だけ使い、並列操作を保証しない。Value使用と同じleaseのDisposeを競合させない。

ローダーの呼び出しスレッドやSynchronizationContextは保証しない。managerはTask.Runや同期ブロックを要求せず、Browserでもawaitで動作する。GPU deviceや音声APIにスレッド制約があるローダーは、自身で対応する実行スレッドへ処理を配送する。異なるキーのLoad／Unloadは並列に呼ばれ得るため、ローダーと取得元が必要な同期を提供する。

[GRAPHICS-0003](../graphics/GRAPHICS-0003-textures-and-views.md) と [GRAPHICS-0007](../graphics/GRAPHICS-0007-command-buffers-and-submission.md) のGPU所有権を変更しない。GPUローダーは明示的にuploadと完了待機を行い、stagingと失敗時の部分生成物を管理する。利用者はcommand記録からsubmission完了までleaseを保持し、Viewなどの子を解放してからleaseを返す。lease返却はGPU使用完了の証明ではない。Unload時の未完了GPU使用や生存中の子は既存契約に従って失敗とし、managerが暗黙にsubmitや待機を補わない。

ファイル取得元は設定したroot内に論理名を解決し、root外への参照とシンボリックリンクによる逸脱を拒否する。HTTP取得元は設定したbase URI内で各セグメントをエスケープして解決し、任意ホストへの要求に使わない。これらのアダプター、具体的な画像／音声／モデル形式、Browserの取得方式は後続の設計と実装で定める。

### エラーと検証方針

無効ID・nullは引数例外、未登録型はNotSupportedException、寿命違反はInvalidOperationException、解放後アクセスはObjectDisposedExceptionとする。I/Oとデコードの例外は原因を保持し、暗黙の代替画像、無限リトライ、エラーの握り潰しを行わない。

実装時はfake取得元とローダーで、同一キーの多重AcquireがLoad一回になること、型・manager間の分離、待機者一人のキャンセルが他者を妨げないこと、キャンセルとlease発行の競合で利用権が漏れないことを検証する。stream解放、ロード失敗後の再試行、最後のlease返却でUnloadしないこと、TrimとAcquireの競合、解放失敗の保持と再試行、終了拒否・終了再試行も確認する。

Windows、Linux、Browserで非同期取得を確認する。GPUローダーではupload失敗時のstaging解放と、記録中・実行中にleaseを返さない利用例を検証する。本PRは文書のみであり、これらの受け入れ条件は実装PRで実行する。

## 検討した代替案

- 各機能が直接読み込みと解放を管理する: 小さなサンプルには適するが、重複読み込みや共有寿命の規則が分散する。
- `Get<T>` で生の値だけを返す: APIは短いが、使用中かどうか判定できず、安全なキャッシュ解放ができない。
- 最後のlease返却時に即時解放する: メモリは残らないが、短期間の再取得でI/Oが増え、同期DisposeにGPU解放や非同期処理が入り込む。
- 汎用IMemoryCacheだけを使う: 保存と期限管理には使えるが、利用中の排除防止、共有読み込み、ローダー固有の解放を別途実装する必要がある。
- 最初から依存グラフと自動予算管理を含める: モデルやmaterialには有用だが、循環検出・派生キー・GPUメモリ測定まで判断が広がるため、基本寿命契約を先に定める。

## 結果と影響

- 取得元と形式ごとのローダーを交換しながら、共有と解放の規則を統一できる。
- 型付きleaseによって利用期間を表せるが、利用側にはleaseを返す責任があり、生のValueの持ち出しを型システムだけでは防げない。
- 明示的なTrimまで未使用値が残るため、アプリはシーン切り替えなどで解放機会を設ける。初期設計には自動メモリ上限の保証がない。
- 呼び出し側キャンセル後も読み込みが続き、不要なI/Oが残る場合がある。共有要求への影響を避けるためのトレードオフとして受け入れる。
- 既存のGraphics APIと所有権は維持する。統合には機能別ローダーと、leaseをGPU使用終了まで保持する利用側の対応が必要になる。

## 別途決定する事項

- アセット間の依存グラフ、循環検出、依存leaseの所有権。
- manifest、バージョン識別、インポート・ビルド・パック形式。
- ホットリロードでの世代切り替えと既存leaseの扱い。
- CPU／GPUメモリ予算、自動排除、優先度、共有読み込み自体の中断。
- 機能別ローダー、取得元アダプター、DI登録、診断基盤への接続。
