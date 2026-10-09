# HTTP 診断アダプター

AddHttpDiagnosticTransport で共通の IDiagnosticTransportFactory を登録する。初期 Hello と Telemetry / Result を POST、要求受信を 2 秒の長いポーリングで実装し、終了時に DELETE する。

HttpDiagnosticTransportOptions は BaseAddress と GameToken。接続は HttpClient を一つ所有し、SessionSecret を X-Diagnostics-Session に付ける。読み取りは一接続一 reader に限定する。共通 JSON は source generation を使い、Int64 は十進文字列。loopback 以外は HTTPS を要求する。

HTTP パッケージは MagicOnion / gRPC を参照しない。Browser 向けに CORS 許可 Origin を [サーバー](../Lumyte.Diagnostics.Server/README.md) で設定できる。検証では .NET HttpClient の実通信と CORS preflight を確認した。Browser / WASM ランタイムでの実行は未検証。

[共通 API](../Lumyte.Diagnostics.Transport/README.md)、[別プロセスでの実行](../../../samples/Lumyte.Diagnostics.Remote.Sample/README.md) を参照する。
