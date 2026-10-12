# ADR-DIAGNOSTICS-0004: 要求時キャプチャを検査するRendering UI

- 状態: 採用
- 日付: 2026-10-11

## 背景

[レンダーグラフ診断](../graphics/GRAPHICS-0015-render-graph.md)は、通常の軽量集計と要求時の詳細キャプチャを分離している。汎用Operationsフォームでも検査できるが、capture IDの保持、状態確認、宣言順と実行順の区別、複数の詳細操作の呼び出しを利用者が管理する必要がある。既存の診断UIへ専用表示を追加し、描画中の負荷を抑えたまま実行計画を調べられるようにする。

このADRは[診断UIの拡張方式](DIAGNOSTICS-0002-server-hosted-ui.md)を使うRenderingページの設計を追加する。既存の診断通信とレンダーグラフの契約を継続する。

## 決定

### Renderingページ

[既存の診断UI](DIAGNOSTICS-0002-server-hosted-ui.md) の Rendering ページを専用コンポーネントとして実装する。Blazor／Fluent UI、認証済み Circuit と既存 Operation 配送を使用し、ゲームとブラウザーの新しい通信路は追加しない。標準 subsystem `graphics.render-graph` の schema version 2 と必要な操作を確認し、未対応のカタログを実行しない。moduleId を変更した場合は汎用 Operations から操作できる。

通常の表示は scope 開始以来の compile／record 件数、CPU 時間の平均と最大値、失敗数、capture 保持状況とする。初期表示で一度だけ集計を読み、手動更新と任意の自動更新を提供する。自動更新は集計だけを最短 1 秒間隔で読む。frame 単位や GPU 時間に読み替えない。表示停止はゲーム側の収集停止を意味しない。

利用者は graph 名（空なら次の任意 graph）で次回 Compile の capture を一度要求し、返された captureId を保持して状態と構造を表示する。既知の captureId を入力して開くこともできる。結果が不明の要求を自動再送せず、待機停止は捕捉要求の取消を意味しない。待機中の状態確認は最短 1 秒間隔、最大 30 回とし、続きは手動更新できる。期限切れ／退避／未知 ID、上限超過、権限拒否、無効化、待機要求の競合を区別して表示する。API が返さない capture 一覧や残り有効秒数は推測しない。

Pass、Resource、Dependency、Barrier は選択したタブだけ 10 行ごとに取得する。全件の先読みをせず、同時に一つの読み取り処理だけを実行する。表示と一時 cache は有界とし、詳細取得は通常集計の自動更新とは分離する。Pass の宣言順と実行順、culled／unused の -1、graph 末尾の export barrier を区別する。resource の FirstPass／LastPass は graph 内の使用範囲として表示し、GPU allocation の寿命とは扱わない。

画面はゲーム・capture の identity を保持し、切替・ページ離脱・認証失効・Circuit 切断で待機を止め、遅延した結果を別の表示へ適用しない。graph、pass、resource 名は Razor のエスケープで描画する。既存ブラウザー検証へ HTTP／MagicOnion の実接続、キャプチャ後の frame 進行、ページ切替、狭い画面の操作とギャラリー撮影を追加する。

### APIと依存関係

既存の7つの診断操作とschema version 2をそのまま利用する。表示モデルはServer内に置き、Graphics実装への参照を追加しない。ゲーム側API、通信DTO、公開ページ拡張APIは変更しない。組み込みコンポーネントとして以下を追加する。比較元は`origin/main`（`81bba9e`）。

```diff
+namespace Lumyte.Diagnostics.Server.Components
+{
+    // 認証済み共通シェルからDynamicComponentとして生成する。
+    public partial class RenderGraphPage : ComponentBase, IAsyncDisposable
+    {
+        // CascadingParameterで選択中ゲームのカタログを受け取る。
+        public DiagnosticPageContext Context { get; set; }
+        // 画面が所有する待機を終了する。繰り返し呼び出しは許容する。
+        public ValueTask DisposeAsync();
+    }
+}
```

### 使用例

1. `AddRenderGraphDiagnostics<TPoint>()`を公開するゲームを選び、EngineのRenderingを開く。累積件数とCPU時間を一度取得する。必要なら1秒ごとの集計更新を有効にする。
2. Graph名を入力して「次の実行をキャプチャ」を押す。返されたcapture IDを保持し、対象のCompileを待つ。
3. Passesでcullingと実行順、Resourcesで使用範囲、DependenciesとBarriersで依存と同期を確認する。各タブの前後ボタンまたはIndex指定で10行ずつ移動する。
4. 別画面へ移った場合は、保存期限内にcapture IDを入力して再度開く。期限切れなどで見つからない場合は新しいキャプチャを明示的に要求する。

GPUなしで再現できる[リモートサンプル](../../../samples/Lumyte.Diagnostics.Remote.Sample/README.md)は、環境変数で有効にした場合だけ実際のレンダーグラフをCompileする。GPU命令は記録しないため、状態はcompiled、Record件数は0のままとする。

## 検討した代替案

- 全frameの詳細を継続配信する: 宣言・snapshot・転送の負荷が常時発生し、要求時キャプチャの方針と合わない。
- 開いたキャプチャの詳細を全件取得する: 大きなgraphでサーバーの保持量とゲーム側operation処理が集中する。10行単位の選択的取得を採用する。
- 汎用Operationsだけを利用する: 既存経路として維持できるが、IDやindexの管理を利用者へ委ねる。標準module用の専用UIを追加する。
- 新しいブラウザー用APIやゲーム直接接続を用意する: 認証・権限・キャンセル・schemaを重複管理するため、既存Circuit経由のoperation配送を用いる。

## 結果と影響

描画を続けたまま一つの実行計画を固定して調べられる。通常表示の更新頻度と詳細の取得量を利用者が制御でき、診断の負荷を抑えられる。一方、複数ページをまたぐ詳細の取得には操作回数が必要になり、ゲーム側の保持期限を超えると取得し直す必要がある。

この画面はCPU計測とgraphの構造を扱う。GPU timestamp、resource内容、replay、複数graphを束ねるframe capture、custom moduleの専用UI選択は別途設計する。検証は表示モデルのschema・上限・identity・キャンセル試験、HTTP／MagicOnionの実接続とブラウザー操作、PC／狭い画面の撮影で行う。
