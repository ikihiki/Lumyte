# ADR-DIAGNOSTICS-0002: 診断サーバーによるWeb UIの配信

- 状態: 採用
- 日付: 2026-10-09

## 背景

[診断通信基盤](DIAGNOSTICS-0001-diagnostics-transport.md) のゲーム接続と操作APIをブラウザーから利用したい。診断サーバー自身がHTMLを配信し、利用者が別のUIサーバーを起動せずに診断を開始できる構成を採用する。

## 決定

ASP.NET Coreの診断サーバーにHTML・CSS・JavaScriptを同梱し、同じオリジンから配信する。ブラウザーはゲームへ直接接続しない。ゲーム側のMagicOnion／HTTPの選択とDI契約を維持する。初期UIはフレームワーク・Nodeビルドを追加せず、小さなJavaScriptモジュールで作る。React／TypeScriptはUIが複雑になった段階で別途判断する。

静的ファイルはassembly resourceとして同梱する。起動ディレクトリに依存せず、dotnet publishの成果物だけで利用できる。UI本体には認証情報・ゲームデータを埋め込まない。

### HTTP APIと認証

| メソッド・パス | 契約 |
| --- | --- |
| GET / | UIのHTML |
| GET /assets/diagnostics.css、/assets/diagnostics.js | 同梱資産。未知の資産は404 |
| GET /api/ui/bootstrap | ログイン状態・CSRFトークンを返す。キャッシュ不可 |
| POST /api/ui/login | OperatorTokenをフォームで検証し、30分のHttpOnly Cookieを発行 |
| POST /api/ui/logout | Cookieを削除。ログアウトは操作やInputリースの取消を意味しない |
| GET /api/ui/sessions | 秘密を除いた既存SessionSnapshot配列 |
| POST /api/ui/sessions/{id}/operations | 既存OperationInvocationを検証して配送。既存の権限・期限・重複排除を利用 |
| DELETE /api/ui/sessions/{id}/connection | ゲーム接続を終了。UIで明示確認を求める |
| GET /api/ui/events?sessionId={id} | SSEのstateイベント。全ゲームの要約と、選択したゲームの最新最大200イベント |

ブラウザー専用APIはCookie専用の認証ポリシーを使用する。既存Bearer APIとゲーム接続にCookieを流用しない。CookieのPathは/api/ui、SameSiteはStrict、更新期限は固定30分。loopback HTTPではSecureを付けず、HTTPSではSecureを付ける。ゲーム接続用トークンやSessionSecretはUIへ返さない。入力したOperatorTokenをJavaScriptストレージやURLへ保存しない。

ログインを含むすべての変更要求をASP.NET Core Antiforgeryで検証する。ログイン後は認証主体に対応するCSRFトークンを再取得する。UI APIのCORSは無効にし、既存の明示CORS設定はBearer API用として維持する。ログイン試行はサーバー全体で毎分10回まで。HTMLにはCSPを設定し、受信したゲームの文字列をHTMLとして解釈しない。

### UIと更新の契約

ゲーム選択、概要、Operations、Input、Metrics、Logs、Tracesを提供する。操作フォームは公開カタログのscalar型・範囲・最大長・revision要件から生成する。操作名・サブシステムの検索を提供し、各画面に生成するフォームを最大20操作に限定する。InputタブにはOverrideInput権限の公開操作を表示する。実入力・有効入力の現在値や解除操作は、ゲームが公開していない限り推測・生成しない。ゲーム接続終了はすべての接続所有リースへ影響するため、個別解除とは区別する。

Int64は既存JSON契約の十進文字列のまま扱う。入力はBigIntで範囲確認し、Numberへ変換しない。権限はサーバーで再検証する。操作の結果が不明になった場合は自動再送しない。新しい操作ごとに新しいRequestIdを生成する。

SSEは毎秒要約を確認する。保持件数・欠落件数・未完了操作・接続構成に変更があった場合にstateを送る。選択ゲームの保持イベントを最大200件のスナップショットで置き換え、再接続による二重追加を避ける。過去全量の履歴や永続的なイベント再生は提供しない。選択変更時は古い購読を閉じ、非表示タブとログアウト時も閉じる。最大16本の同時SSE接続を許可し、超過は429。各接続は最大2分で終了し、再接続時に認証を再検証する。

## 検討した代替案

- 別のフロントエンドサーバー: 配布・起動・認証・CORS設定が増えるため採用しない。
- ゲームへのブラウザー直接接続: 通信方式の抽象と診断サーバーの権限管理を迂回するため採用しない。
- UIへのBearerToken埋め込み・localStorage保存: HTML配布と資格情報を分離できないため採用しない。
- 全テレメトリーを各画面へ無制限に送信: 初期UIには不要で、サーバー・ブラウザーの負荷を増やすため採用しない。

## 結果と影響

一つの診断サーバーのURLから利用でき、既存ゲームの公開OperationをUI変更なしで操作できる。更新は選択対象と有界スナップショットに限定する。一方でCookie認証・CSRF・静的資産・SSEの管理がサーバーへ加わる。

初期実装はloopback開発用途を維持する。外部ID基盤、HTTPS公開ホスティング、永続監査、Metricsの時系列集約、Traceタイムライン、オブジェクトグラフ、描画結果の取得は後続範囲。実装・検証結果は[サーバーREADME](../../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)に記録する。
