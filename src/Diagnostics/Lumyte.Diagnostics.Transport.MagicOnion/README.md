# MagicOnion 診断アダプター

AddMagicOnionDiagnosticTransport で共通の IDiagnosticTransportFactory を登録する。MagicOnion 7.11.0 の StreamingHub がコマンドを push し、Game は JoinAsync でカタログを公開し、PublishAsync で結果・標準 Telemetry・Heartbeat を送る。

MagicOnionDiagnosticTransportOptions は Endpoint と GameToken。HTTP/2 エンドポイントを使い、loopback の開発用 h2c に対応する。loopback 以外は HTTPS を要求する。接続が GrpcChannel と Hub を所有し、DisposeAsync で解放する。受信キューは 256 件で、満杯なら接続失敗として通知する。読み取りは一接続一 reader に限定する。

publication は共通 DiagnosticMessage を引数とし、DiagnosticMessagePack.Options に登録した DiagnosticMessageFormatter が numeric key 順で直接書く。送信時に WireMessage / WireEvent / WireResult / WireValue を構築しない。受信も共通モデルへ直接復号し、WireMessage / WireEvent / WireResult は本番から削除した。GUID も UTF-8 形式で出力バッファへ直接書く。低頻度の Hello / Welcome / Command / Receipt は wire DTO と generated resolver を使う。共通モデルとエンジンには MessagePack 属性を持ち込まない。受信時は 128 events・36 event fields・32 result fields の上限をコレクション確保前に検証し、重複キーを拒否する。未知の末尾フィールドも depth 上限を適用して読み飛ばす。シリアライザーの depth は 32、gRPC メッセージは 4 MiB の上限。JSON を包んで送る実装ではなく、scalar・カタログ・イベントを native MessagePack フィールドへ書く。

実行時の Hub クライアント生成は MagicOnion の既定 factory を使う。Operation は無反射生成だが、通信クライアントの NativeAOT 対応は今回の検証範囲に含まない。

[共通 API](../Lumyte.Diagnostics.Transport/README.md)、[サーバー](../Lumyte.Diagnostics.Server/README.md)、[実通信の検証](../../../docs/diagnostics/communication-verification.md) を参照する。
