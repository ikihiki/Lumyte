# 診断サーバー

ASP.NET Core / Kestrel で起動する診断サーバー。ゲーム側の接続とカタログを管理し、同梱のWeb UIまたはREST APIからOperationを要求する。ゲームは MagicOnion StreamingHub または HTTP 長いポーリングで受信し、同じ DI / Pump / Generator を使って実行する。

## 起動

.NET SDK はリポジトリの global.json に従う。ルートから実行する。ゲーム用と操作用に別の十分な長さの認証トークンを環境変数で設定する。値をログへ出力しない。

```sh
export LUMYTE_DIAGNOSTICS_GAME_TOKEN="$(openssl rand -hex 32)"
export LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN="$(openssl rand -hex 32)"
dotnet run --project src/Diagnostics/Lumyte.Diagnostics.Server -c Release
```

この端末に設定した GameToken を、ゲームを起動する端末にも設定する。既定は **127.0.0.1:5000 の HTTP/1** と **127.0.0.1:5001 の HTTP/2**。後者はローカル開発用の h2c。ポートは LUMYTE_DIAGNOSTICS_HTTP_PORT / LUMYTE_DIAGNOSTICS_GRPC_PORT で変更できる。資格情報なし、両トークンが同じ、無効なポートでは起動を拒否する。

ブラウザーの別 Origin から利用する場合は LUMYTE_DIAGNOSTICS_ALLOWED_ORIGINS に許可する Origin をカンマ区切りで設定する。Authorization / Content-Type / X-Diagnostics-Session の CORS preflight に対応する。空ならcross-originを許可しない。同梱UIの認証とSignalR接続はこの設定によらず同じOrigin専用とする。

## Web UI

**[http://127.0.0.1:5000/](http://127.0.0.1:5000/)** を開き、起動時に設定したOperatorTokenを入力する。HTML・CSS・JavaScriptは診断サーバー自身が配信する。別のUIサーバー、npm install、NodeでのUIビルドは不要。Blazor Server（Interactive Server）とFluent UIを使う。Razorコンポーネントはassembly、自前CSSは埋め込みresource、Blazor／Fluent UIの資産はstatic web assetsとして同梱し、次のpublish成果物だけで起動できる。

```sh
dotnet publish src/Diagnostics/Lumyte.Diagnostics.Server -c Release -o artifacts/diagnostics-ui-server
dotnet artifacts/diagnostics-ui-server/Lumyte.Diagnostics.Server.dll
```

Aspire DashboardのResources・構造化ログ・相関移動を参考に、Resources一覧または左のゲーム一覧から対象を選び、OperationsまたはInputタブで公開された操作の引数を入力する。フォームはカタログのBoolean／Int64／Double／String、範囲、最大長、revision要件から生成する。操作名・サブシステムで検索でき、各画面のフォームは最大20操作に限定する。InputタブはOverrideInput権限の操作を表示する。サンプルではbutton=Jump、pressed=true、duration-ms=5000を入力して実行できる。返ったリースIDと成功・拒否結果を右のInspectorへ表示する。

Metrics／Logs／Tracesは選択したゲームの最新最大200件を表示し、名前・フィールド・Trace IDで検索できる。イベント名をクリックすると詳細を表示する。Int64はC#のlongで扱い、グラフはBigIntegerで差分を計算する。画面はDIの読み取りサービスから変更通知を購読し、連続変更を200 msでまとめる。SignalRはBlazorの画面差分を運び、ゲーム側のMagicOnion／HTTPには影響しない。カタログは接続構成の変更時、イベントは選択ゲームの更新時に読む。

操作用トークンはURLやブラウザーストレージへ保存しない。ログイン後はPath=/、SameSite=Strict、HttpOnly、固定30分のCookieを使う。ログイン・ログアウトのHTTPフォームはCSRF検証を行い、SignalRは認証と同一Originの検査を行う。ゲーム用トークン・SessionSecretを画面へ返さず、既存Bearer APIはCookieを受け付けない。ログインはサーバー全体で毎分10回、認証チケットは最大128個、Circuitは最大16個、切断したCircuitの保持は10秒に制限する。

切断時は購読と操作待機を取消し、接続復帰時に認証を確認して購読を再開する。操作は自動再送しない。認証期限切れとログアウトは同じログインに属するすべてのCircuitを失効させ、表示データを除く。ゲームで実行済み・配送済みの操作の巻き戻しは意味しない。ログアウトはInput操作の取消ではない。「ゲーム接続を終了」は画面上の確認後に接続を閉じ、その接続のリースをゲーム側で解除する。

ゲームが公開していない実入力の現在値、個別解除、オブジェクトグラフ、描画結果はUIで推測しない。構造化ログは最小レベルとTrace IDで絞り込み、表示を一時停止できる。停止中もサーバーの収集は続き、再開すると最新のスナップショットを表示する。Trace IDから関連Logs／Tracesへ移動でき、対象ゲーム・画面・Trace IDはURL queryで共有・再読み込みできる。切断済みのリンク先を別ゲームへ自動変更しない。

Tracesは保持Spanの親子関係と処理時間を表示する。バーは最長Spanとの比率で、欠けた親は明示する。Metricsは名前・値の型・タグで分けた最新観測値と受信順のグラフを表示する。Counterの累積・レートやHistogram分布を推測しない。完全な時系列集約やwall-clockのTrace waterfall、OTLP受信、SQLストアは後続範囲。

### UI検証

統合テストでHTML・資産・CSP・Cookie／Bearer分離・CSRF・ログアウトとCookie再利用の拒否、両ゲーム通信方式の操作、200件の保持末尾と対象分離、Circuitとログインの上限、切断・再接続・認証失効による購読の取消を確認する。C#の表示モデルとフォームのテストでログ絞り込み、Span階層と循環、タグ別系列、Int64グラフの精度、duration、引数の型・範囲・長さ・revision、認証期限を確認する。

Chromiumを使う別プロセス検証は、上記のpublish後に次を実行する。ゲームサンプルのReleaseビルドも必要。Nodeはこの検証だけに使用する。CHROMEでChrome／Chromiumの実行ファイル、DOTNETでdotnetのパスを変更できる。

```sh
dotnet test tests/Diagnostics/Lumyte.Diagnostics.IntegrationTests -c Release
dotnet build samples/Lumyte.Diagnostics.Remote.Sample -c Release
node tools/diagnostics/verify-browser.mjs
```

一時トークンで公開サーバーをソースツリー外から起動し、実ブラウザーでログイン、BlazorのSignalR WebSocket、Fluent UIコンポーネント、生成フォーム、Input変更、Int64 Metric、Log／Trace相関、切断時解除、ログアウト、狭い画面での表示を確認する。複数リソースの分離、ログレベル、一時停止・再開、Traceリンクの再読み込み、親子Span、相関Logs、大きいInt64のグラフ、HTML文字列の安全な表示、別タブのCircuit失効も確認する。結果・スクリーンショット・ログはartifacts/test-results/diagnostics-uiへ出力する。CIのLinux x64でも実行する。[検証結果](../../../docs/diagnostics/results/browser-ui-processes.json)と[UI ADR](../../../docs/adr/diagnostics/DIAGNOSTICS-0002-server-hosted-ui.md)を参照する。

## ゲームを接続

```sh
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- magiconion http://127.0.0.1:5001 60
# 同じコードを HTTP で起動する場合
dotnet run --project samples/Lumyte.Diagnostics.Remote.Sample -c Release -- http http://127.0.0.1:5000 60
```

[ゲームサンプル](../../../samples/Lumyte.Diagnostics.Remote.Sample/README.md) を参照する。カタログは接続時に一括公開する。サーバーは protocolVersion = 1 を受け入れ、新しい SessionId と SessionSecret、許可する権限を返す。ゲーム用の enrollment トークンと、HTTP のセッション操作に必要な秘密の SessionSecret は別である。MagicOnion は参加済み Hub のセッションへ操作を限定する。

## 操作 API

OperatorToken で認証する。以下はシェル変数の値を表示せずに利用する例。

```sh
curl --fail --silent -H "Authorization: Bearer $LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN" \
  http://127.0.0.1:5000/diagnostics/v1/sessions
```

返された sessionId を SESSION_ID に設定し、Input 操作を送る。RequestId は新しい論理操作ごとに生成する。

```sh
REQUEST_ID="$(python3 -c 'import uuid; print(uuid.uuid4())')"
curl --fail --silent -H "Authorization: Bearer $LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"requestId\":\"$REQUEST_ID\",\"subsystemId\":\"input\",\"operationId\":\"override-button\",\"arguments\":{\"button\":{\"kind\":3,\"string\":\"Jump\"},\"pressed\":{\"kind\":0,\"boolean\":true},\"duration-ms\":{\"kind\":1,\"int64\":\"1000\"}},\"timeoutMilliseconds\":5000}" \
  "http://127.0.0.1:5000/diagnostics/v1/sessions/$SESSION_ID/operations"
```

成功時は status = success と values.lease-id が返り、ゲーム端末で `Input state: Jump=True` を確認できる。長さ 1,000 ms のリースが切れると False に戻る。すべての Int64 は JSON で十進文字列にし、JavaScript の Number の精度に依存しない。kind は Boolean = 0 / Int64 = 1 / Double = 2 / String = 3。

| メソッド・パス | 呼び出し主体 | 動作 |
| --- | --- | --- |
| POST /diagnostics/v1/sessions | Game | 認証・バージョン確認・カタログ公開 |
| GET /diagnostics/v1/sessions | Operator | セッションとカタログ一覧。SessionSecret は含まない |
| GET /diagnostics/v1/sessions/{id}/commands | Game + SessionSecret | 2 秒の長いポーリングで未完了要求を配送 |
| POST /diagnostics/v1/sessions/{id}/messages | Game + SessionSecret | 結果・Telemetry・Heartbeat の受理 |
| DELETE /diagnostics/v1/sessions/{id} | Game + SessionSecret | 自分のセッション終了 |
| POST /diagnostics/v1/sessions/{id}/operations | Operator | 要求を配送し、RequestId に対応する結果を待つ |
| GET /diagnostics/v1/sessions/{id}/telemetry | Operator | 保持中の Metrics / Trace / Log のスナップショット |
| DELETE /diagnostics/v1/sessions/{id}/connection | Operator | 対象セッションを閉じる |

401 は認証失敗、403 は役割・セッション資格違反、404 はセッション消失、400 は不正なプロトコル入力。個別の操作拒否は JSON の status / code で返す。ActorId は認証済み主体からサーバーが設定し、利用者の自己申告を受け取らない。GamePermissions と OperatorPermissions が Operation の RequiredPermission を両方許可する場合に配送する。

## 上限と終了

- サーバー最大 64 セッション。カタログ 256 KiB、Invocation 64 KiB、HTTP 要求／gRPC メッセージ 4 MiB の上限。
- 各セッションの未完了操作 64 件、結果キャッシュ 256 件かつ Invocation の符号化サイズ合計 1 MiB。完了要求は期限から 30 秒後まで保持し、投入時に期限切れ完了エントリーを除去する。
- RequestId の同一内容・同一主体は元の結果を返す。Dictionary の順序を正規化し、異なる内容の ID 再利用は request-id-conflict。HTTP は結果の受理まで未完了要求を再配送でき、ゲームの Pump も重複を排除する。
- MessageId の受理結果を最大 1,024 件保持する。同じ内容は前の receipt を返し、異なる内容は message-id-conflict。永続化・無期限の exactly-once は提供しない。
- Telemetry は 1 バッチ最大 128 件。各セッション最大 1,024 件と推定保持量 4 MiB で古いイベントを取り除き、TelemetryDropped に数える。推定量は文字列とフィールド数を基にした予算で、実際の heap 使用量の測定値ではない。
- ゲームの共通エージェントは 100 ms ごとに収集キューを drain し、idle 時も 1 秒ごとに Heartbeat を送る。30 秒間通信がなければセッションを閉じる。期限・idle は 5 秒ごとの sweep でも確認する。
- 要求の結果が期限までに届かない場合は expired とする。副作用が実行されなかったという保証ではなく、結果が不明な操作を自動再実行しない。
- 通信終了で共通エージェントの RunAsync が完了する。ゲームの所有スレッドが Input の ReleaseSession と Pump.Deactivate を実行する。サンプルは finally でこの順序を保証する。

サーバーは loopback で動く開発用の in-memory 実装。外部 ID プロバイダー、TLS 公開ホスティング、永続保存・監査、グラフ・画像転送は後続範囲。HTTP クライアントと MagicOnion クライアントは、loopback 以外への平文認証送信を拒否する。

[実通信の検証結果・再現スクリプト](../../../docs/diagnostics/communication-verification.md) と [ADR](../../../docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md) を参照する。

内部保持用のコピーは scalar / array / dictionary を直接コピーする。コピー目的の JSON 往復は行わない。List / Telemetry の外向きコピーは共有 lock の外へ移し、返却した権限・要求・結果を変更しても内部状態に影響しない。メッセージ重複の 1,024 件キャッシュは受信順の明示 FIFO で保持する。
