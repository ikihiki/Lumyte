# 診断サーバーの実通信検証

検証日: 2026-10-09。Linux x64、.NET SDK 10.0.401 / Runtime 10.0.12、MagicOnion 7.11.0、MessagePack 3.1.11、gRPC 2.71.0。

診断サーバーを Kestrel で起動し、ゲームをクライアントとして接続した。MagicOnion は HTTP/2 StreamingHub、HTTP は JSON と長いポーリングを使う。MagicOnion のペイロードは native MessagePack であり、JSON のラップではない。操作利用者はどちらの場合もサーバーの HTTP API を使用する。

## 確認した挙動

- 生成した Input Operation のカタログをサーバーで取得できる。
- サーバーから override-button を要求すると、ゲームの所有スレッドの Pump が実行し、Jump の値が変わる。
- RequestId と操作結果を対応させ、同じ要求の再送は同じリースを返す。内容を変えた ID 再利用は拒否する。
- Meter / ActivitySource / ILogger の Metric、Span、Log が届く。Trace と Log の TraceId が一致する。
- Int64 の 9007199254740993 が欠損なく届く。HTTP JSON では decimal string とする。
- 複数ゲームの操作・テレメトリーを分離する。
- サーバーが接続を終了すると、ゲームが停止を検知して Input のリースを解除する。
- 無認証、誤ったゲームトークン、ゲームによる操作利用者 API の呼び出し、HTTP セッション秘密の不一致を拒否する。
- 非互換プロトコル、期限切れ操作、重複メッセージの内容変更を拒否し、許可した CORS origin の preflight に応答する。

IntegrationTests の実通信 5 ケースは TestServer や通信モックを使わず、動的ポートの実 TCP 接続を使う。さらにサーバーとゲームを独立プロセスとして起動し、Python の操作利用者から両方式を確認した。[別プロセスの結果](results/communication-smoke-results.json) に PID と確認項目を保存する。

## 再現

リポジトリで SDK を有効化して実行する。

```sh
dotnet build tests/Diagnostics/Lumyte.Diagnostics.IntegrationTests -c Release
dotnet build samples/Lumyte.Diagnostics.Remote.Sample -c Release
dotnet test tests/Diagnostics/Lumyte.Diagnostics.IntegrationTests -c Release --no-build
python3 tools/diagnostics/communication-smoke.py
```

スクリプトは一時トークンと loopback ポートを選び、子プロセスを終了させる。結果とログは artifacts/diagnostics-communication/processes に出力する。CI は solution の統合テストに加え、Linux でこのスクリプトを実行する。手動起動と curl は [サーバー README](../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)、DI の切り替えは [ゲーム README](../../samples/Lumyte.Diagnostics.Remote.Sample/README.md) を参照する。

## 範囲

これは実通信の機能確認であり、直接エンコード追加後も両方式の独立プロセス確認が成功した。[今回の結果](results/direct-encoding-processes.json) を参照する。wire 一致・結果の直接検証など 9 ケースも追加し、統合テストは計 14 件となる。基盤・Generator の 18 件も成功した。

スクリプトの単発操作時間はネットワーク性能のベンチマークではない。[シリアライズ比較](../benchmarks/diagnostics/README.md) は別のローカル測定である。

Input はリースを持つサンプルモデルで、既存 InputSystem のデバイス入力へはまだ統合していない。実オブジェクトグラフ、描画キャプチャー、転送、購読・集約、再接続、Browser/WASM、NativeAOT、Windows 実行は未検証または未実装。サーバーは開発用のメモリー保持と固定トークン認証で、公開ホストの TLS・外部 ID 基盤・永続監査は含まない。HTTP のリクエストと gRPC の個々のメッセージは 4 MiB に制限し、キュー・セッション・保持件数も制限する。
