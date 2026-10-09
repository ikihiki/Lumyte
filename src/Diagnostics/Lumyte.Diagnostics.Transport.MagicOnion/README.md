# MagicOnion 診断アダプター

AddMagicOnionDiagnosticTransport で共通の IDiagnosticTransportFactory を登録する。MagicOnion 7.11.0 の StreamingHub がコマンドを push し、Game は JoinAsync でカタログを公開し、PublishAsync で結果・標準 Telemetry・Heartbeat を送る。

MagicOnionDiagnosticTransportOptions は Endpoint と GameToken。HTTP/2 エンドポイントを使い、loopback の開発用 h2c に対応する。loopback 以外は HTTPS を要求する。接続が GrpcChannel と Hub を所有し、DisposeAsync で解放する。受信キューは 256 件で、満杯なら接続失敗として通知する。読み取りは一接続一 reader に限定する。

WireHello / WireWelcome / WireSubsystem / WireOperation / WireField / WireValue / WireCommand / WireResult / WireEvent / WireMessage / WireReceipt を numeric key の MessagePack DTO とし、generated resolver で符号化・復号する。共通モデルとエンジンには MessagePack 属性を持ち込まない。シリアライザーの depth は 32、gRPC メッセージは 4 MiB の上限。JSON を包んで送る実装ではなく、scalar・カタログ・イベントを native MessagePack フィールドへ明示的にマッピングする。

実行時の Hub クライアント生成は MagicOnion の既定 factory を使う。Operation は無反射生成だが、通信クライアントの NativeAOT 対応は今回の検証範囲に含まない。

[共通 API](../Lumyte.Diagnostics.Transport/README.md)、[サーバー](../Lumyte.Diagnostics.Server/README.md)、[実通信の検証](../../../docs/diagnostics/communication-verification.md) を参照する。
