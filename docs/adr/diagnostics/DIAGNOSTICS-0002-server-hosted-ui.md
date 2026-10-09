# ADR-DIAGNOSTICS-0002: 診断サーバーによるWeb UIの配信

- 状態: 採用
- 日付: 2026-10-09

## 背景

[診断通信基盤](DIAGNOSTICS-0001-diagnostics-transport.md) のゲーム接続と操作APIをブラウザーから利用したい。診断サーバー自身がHTMLを配信し、利用者が別のUIサーバーを起動せずに診断を開始できる構成を採用する。

## 決定

ASP.NET Coreの診断サーバーにBlazor Server（Interactive Server）とFluent UIを同梱し、同じオリジンからHTMLと資産を配信する。ブラウザーはゲームへ直接接続しない。ゲーム側のMagicOnion／HTTPの選択とDI契約を維持する。画面、フォーム、表示クエリーはC#で実装し、ブラウザーとの更新・操作はBlazorのSignalR接続へ閉じ込める。Nodeによるフロントエンドビルドは追加しない。

Razorコンポーネントはサーバーassembly、Fluent UI／Blazorの資産はstatic web assets、自前CSSはassembly resourceとして同梱する。起動ディレクトリに依存せず、dotnet publishの成果物だけで利用できる。

### HTTPと認証

未認証のGET /はログインフォームだけを静的レンダリングする。GET /api/ui/bootstrapはHTTPフォーム利用者へ認証状態とCSRFトークンを返す。認証済みの場合だけ診断画面のInteractive Server接続を起動する。POST /api/ui/loginはOperatorTokenを検証し、固定30分のHttpOnly、SameSite Strict Cookieを発行する。CookieのPathは/とし、Blazorの初期HTTP要求とSignalR接続へ適用する。既存Bearer APIとゲーム接続は引き続き専用認証を使い、Cookieを受け付けない。loopback HTTPではSecureを付けず、HTTPSではSecureを付ける。ゲーム接続用トークンやSessionSecretは画面へ返さない。OperatorTokenをブラウザーストレージやURLへ保存しない。

ログインとログアウトは通常のHTTPフォームとASP.NET Core Antiforgeryで検証する。SignalRの接続もCookieとOriginを検証する。ブラウザー用のCORSは無効にし、既存の明示CORS設定はBearer API用として維持する。ログイン試行はサーバー全体で毎分10回まで。HTMLと資産にはCSPを設定し、受信したゲームの文字列はRazorのエスケープで表示する。

操作用の認証チケットをサーバー側で最大128個保持し、ログアウトでチケットを失効させる。同じログインに属する他のCircuitにも取消を通知する。画面の読み取り、操作、接続終了はDIのCircuit scopedサービスが認証チケット・接続状態を確認する。認証期限切れは購読と進行中の待機を取消し、画面から診断データを除く。ログアウト自体はゲーム接続やInputリースを終了しない。

### UIと更新の契約

Resourcesを入口にゲームを選び、Operations、Input、Metrics、Logs、Tracesへ移動する。操作フォームは公開カタログのscalar型・範囲・最大長・revision要件から生成する。操作名・サブシステムの検索を提供し、各画面に生成するフォームを最大20操作に限定する。InputタブにはOverrideInput権限の公開操作を表示する。実入力・有効入力の現在値や解除操作は、ゲームが公開していない限り推測・生成しない。ゲーム接続終了はすべての接続所有リースへ影響するため、個別解除とは区別する。

操作入力はC#のBoolean、Int64、Double、Stringへ検証して変換する。Int64はlongで扱い、グラフの差分はBigIntegerで正規化する。JavaScriptのNumberへ変換しない。権限は既存Registryで再検証する。操作の結果が不明になった場合は自動再送しない。新しい操作ごとに新しいRequestIdを生成する。

画面はDIに登録した読み取りサービスのバージョン付き変更通知を購読し、連続変更を200 msでまとめる。サブスクリプションは全履歴をキューへ積まず、選択ゲームの最新最大200件で置き換える。リソースのカタログは接続構成が変わったとき、表示データは選択リソースが変化したときだけ読む。表示停止中もサーバーで収集するが画面の保持データは固定し、再開時に最新スナップショットを読む。

最大16個の同時Circuit、切断したCircuitの保持は10秒に制限する。切断中は通知待機と操作待機を取消す。接続復帰で認証を再確認して購読を再開し、操作を再送しない。Circuitの破棄時は購読、取消トークンと枠を解放する。SSEとブラウザー専用の操作・一覧APIは廃止し、フォーム認証だけをHTTP境界として残す。

### DIの内部境界

比較元はorigin/main（6717920）。これらはサーバー内部の契約とし、ゲームのサブシステムへBlazorやSignalRの依存を追加しない。

```diff
+namespace Lumyte.Diagnostics.Server
+{
+    // Singleton。秘密を除いたコピーを返し、取消可能な変更通知を共有する。
+    internal interface IDiagnosticDashboardReader
+    {
+        long Version { get; }
+        SessionSnapshot[] Resources();
+        DiagnosticUiSession[] Summaries();
+        DiagnosticEvent[] Events(Guid sessionId);
+        Task<long> WaitForChangeAsync(long version, CancellationToken cancellationToken);
+    }
+
+    // Circuit scoped。画面がRegistryを直接使用することを避ける。
+    internal sealed class DiagnosticDashboardSession : CircuitHandler, IDisposable
+    {
+        // 認証主体はAuthenticationStateProviderから受け取り、HTTP要求を保持しない。
+        public void Attach(ClaimsPrincipal user);
+        // 各読み取りで認証チケットと接続状態を検証する。
+        public SessionSnapshot[] Resources();
+        public DiagnosticUiSession[] Summaries();
+        public DiagnosticEvent[] Events(Guid id);
+        // 認証失効・接続切断・呼び出し側の取消を結合する。
+        public Task WaitForChangeAsync(long version, CancellationToken cancellationToken);
+        // Actorは認証主体から設定する。取消は配送済み操作の巻き戻しを意味しない。
+        public Task<DiagnosticOperationResult> InvokeAsync(Guid id, OperationInvocation invocation, CancellationToken cancellationToken);
+        public void Close(Guid id);
+        // Circuit枠と接続の取消トークンを解放する。複数回の呼び出しは許容する。
+        public void Dispose();
+    }
+}
```

表示コンポーネントが所有する購読は接続切断で停止し、復帰時に新しく作る。Circuit scopedサービスは接続状態と認証の検証、既存Registryへの操作配送を担当する。

### Aspire Dashboardを参考にした構成

Aspire Dashboard v13.6.1の公式実装を参照する。AspireのUIはBlazor／Fluent UI、テレメトリー受信はOTLP、保持と読み取りはrepository、画面状態はViewModel、更新は購読とthrottleで分離されている。本サーバーへ取り込む対応は以下とする。

| Aspireの構成・操作 | 本サーバーへの適用 |
| --- | --- |
| Resourcesの一覧と診断画面へのリンク | Game SessionをResourceとして表示し、対象を保ったままLogs／Traces／Metricsへ移動 |
| ITelemetryRepositoryのqueryとsubscription | DIのIDiagnosticDashboardReaderでリソース・有界イベントの読み取りと変更待機を分離。操作配送は既存Registryが担当 |
| StructuredLogsのseverity・field filter・pause | 最小ログレベル、全文／フィールド、Trace IDの絞り込みと表示停止。停止中もサーバーの収集は継続 |
| Trace詳細からLogsへの相関移動 | Trace IDをURL query、ResourceをURL pathに保持し、相互移動・再読み込みを可能にする。リンク先が切断済みなら診断対象を未選択とし、別ゲームへ操作対象を自動変更しない |
| Span詳細と階層 | 保持中のSpanの親子関係、欠けた親、処理時間、最長Spanに対する比率を表示。循環する入力も有限に処理 |
| InstrumentとtagごとのMetrics | 名前・値の型・タグで系列を分け、保持中の観測値を受信順で表示 |

UIの表示クエリー、系列化、階層化は独立したC#の表示モデルに置く。SpanのDurationTicksは.NET TimeSpanの100 ns単位として表示する。現在のTimestampはゲームの単調時計で、周波数・UTC開始時刻を受信しないため、正確なwall-clock時間軸やwaterfall開始位置は表示しない。Metricsは保持中の測定値を表示し、Counterの累積値やレート、Histogramの分布へ推測変換しない。Int64のグラフはBigIntegerの差分を正規化し、大きな絶対値をNumberへ変換しない。

Blazor／Fluent UIを画面基盤に採用する。OTLP受信、SQLストア、完全なTrace・Metrics集約は別途判断する。

参照した公式ソース:

- [WebApplicationとDI](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/DashboardWebApplication.cs)
- [Resources](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/Components/Pages/Resources.razor)
- [StructuredLogs](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/Components/Pages/StructuredLogs.razor)
- [TraceDetail](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/Components/Pages/TraceDetail.razor)
- [Telemetry queryと購読](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/Otlp/Storage/ITelemetryRepository.cs)
- [Subscriptionの解放・throttle](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Dashboard/Otlp/Storage/Subscription.cs)

## 検討した代替案

- 素のHTML／JavaScriptとSSE: 初期構成は小さいが、オブジェクトグラフ・プロパティ編集・フォームを拡張すると状態管理とC#との型整合を個別実装する必要がある。C#のコンポーネントを共有できるBlazor Serverを採用する。
- Blazor WebAssembly: クライアント配布とAPI境界の追加が必要になる。本用途ではサーバー内DIサービスを直接利用するInteractive Serverを選ぶ。
- 別のフロントエンドサーバー: 配布・起動・認証・CORS設定が増えるため採用しない。
- ゲームへのブラウザー直接接続: 通信方式の抽象と診断サーバーの権限管理を迂回するため採用しない。
- UIへのBearerToken埋め込み・localStorage保存: HTML配布と資格情報を分離できないため採用しない。
- 全テレメトリーを各画面へ無制限に送信: 初期UIには不要で、サーバー・ブラウザーの負荷を増やすため採用しない。

## 結果と影響

一つの診断サーバーのURLから利用でき、既存ゲームの公開OperationをUI変更なしで操作できる。更新は選択対象と有界スナップショットに限定する。一方でCookie認証・CSRF・静的資産・Circuitごとの画面状態がサーバーへ加わる。

初期実装はloopback開発用途を維持する。外部ID基盤、HTTPS公開ホスティング、永続監査、wall-clockでのMetrics集約、完全なTrace waterfall、オブジェクトグラフ、描画結果の取得は後続範囲。実装・検証結果は[サーバーREADME](../../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)に記録する。

### ページの拡張とナビゲーション

ページは DI へ登録した定義から構成する。共通シェルがゲーム選択、認証、接続、カテゴリ別ナビゲーションを所有する。Resources は全体一覧、Overview は選択したゲームの概要とする。Engine に Objects、UI、Animation、Input、Rendering、Telemetry に Metrics、Logs、Traces を配置し、Operations は専用ページの有無によらず全操作を公開する。

ゲーム別 URL は `/games/{sessionId}/{pageId}`、全体一覧は `/resources` とする。Trace ID はクエリに保持する。切断済みゲームのリンクから別のゲームへ自動切替しない。Inspector は操作ページ、または詳細選択時に表示する。

公開カタログの subsystem ID を capability 判定に使用する。未対応と対応済み・データなしを区別し、表示の一時停止は収集停止を意味しない。UI、Animation のグラフ、画像、タイムラインは専用プロトコルの追加後に実装する。現時点ではカタログと実行可能な操作を表示する。

```diff
+namespace Lumyte.Diagnostics.Server
+{
+    // Component は任意の Blazor コンポーネント。未指定なら共通カタログ画面。
+    public sealed record DiagnosticPageDefinition(
+        string Id, string Title, string Category, int Order,
+        string? RequiredSubsystem = null, Type? Component = null);
+    // 拡張コンポーネントへの CascadingParameter。操作結果は共通 Inspector で表示。
+    public sealed record DiagnosticPageContext(SessionSnapshot? Resource,
+        IReadOnlyList<DiagnosticEvent> Events, EventCallback<OperationInvocation> Execute);
+    // 組み込み定義と追加定義をまとめる。ID の重複や不正なコンポーネントは起動時に拒否。
+    public sealed class DiagnosticPageRegistry
+    {
+        public DiagnosticPageRegistry(IEnumerable<DiagnosticPageDefinition> pages);
+        public IReadOnlyList<DiagnosticPageDefinition> Pages { get; }
+    }
     public static class DiagnosticServerApplication
     {
-        public static WebApplication Create(string[] args, Action<DiagnosticServerOptions> configure);
+        // configureServices で singleton のページ定義とページ用 DI サービスを登録する。
+        public static WebApplication Create(string[] args,
+            Action<DiagnosticServerOptions> configure,
+            Action<IServiceCollection>? configureServices = null);
+    }
+}
```

追加ページのコンポーネントは通常の Blazor DI を使用する。ゲーム側の公開 API は画面や通信層に依存しない。組み込みページは共通シェルの認証済み Circuit を利用し、追加ページも認証済みシェル内で描画する。
