# 診断サーバー

ASP.NET Core / Kestrel で起動する診断サーバー。ゲーム側の接続とカタログを管理し、REST API から Operation を要求する。ゲームは MagicOnion StreamingHub または HTTP 長いポーリングで受信し、同じ DI / Pump / Generator を使って実行する。

## 起動

.NET SDK はリポジトリの global.json に従う。ルートから実行する。ゲーム用と操作用に別の十分な長さの認証トークンを環境変数で設定する。値をログへ出力しない。

```sh
export LUMYTE_DIAGNOSTICS_GAME_TOKEN="$(openssl rand -hex 32)"
export LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN="$(openssl rand -hex 32)"
dotnet run --project src/Diagnostics/Lumyte.Diagnostics.Server -c Release
```

この端末に設定した GameToken を、ゲームを起動する端末にも設定する。既定は **127.0.0.1:5000 の HTTP/1** と **127.0.0.1:5001 の HTTP/2**。後者はローカル開発用の h2c。ポートは LUMYTE_DIAGNOSTICS_HTTP_PORT / LUMYTE_DIAGNOSTICS_GRPC_PORT で変更できる。資格情報なし、両トークンが同じ、無効なポートでは起動を拒否する。

ブラウザーの別 Origin から利用する場合は LUMYTE_DIAGNOSTICS_ALLOWED_ORIGINS に許可する Origin をカンマ区切りで設定する。Authorization / Content-Type / X-Diagnostics-Session の CORS preflight に対応する。空なら cross-origin を許可しない。ブラウザー用 UI と資格情報の配布機構は含まない。

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

サーバーは loopback で動く開発用の in-memory 実装。診断 UI、外部 ID プロバイダー、TLS 公開ホスティング、永続保存・監査、グラフ・画像転送は後続範囲。HTTP クライアントと MagicOnion クライアントは、loopback 以外への平文認証送信を拒否する。

[実通信の検証結果・再現スクリプト](../../../docs/diagnostics/communication-verification.md) と [ADR](../../../docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md) を参照する。
