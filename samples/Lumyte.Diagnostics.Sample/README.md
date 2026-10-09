# 診断 Input サンプル

```sh
dotnet run --project samples/Lumyte.Diagnostics.Sample -c Release
```

DI スコープ内の `InputOverrideService` を通常の Input 読み取りと生成された `InputDiagnostics` が共有する。`override-button` を投入し、入力処理前の所有スレッドで Pump を呼び、`Jump overridden: True` と lease-id を確認できる。その後、標準 Counter / ActivitySource / ILogger が生成した metric / log / span の JSON を出力する。

`InputDiagnostics` には Operation 属性一つだけを付ける。引数と戻り値は名前・型から推論する。public な `IDiagnosticContributor.Configure` は Generator が明示実装する。

このサンプルの Input は実行可能な小さなドメインモデルであり、`Lumyte.Input.InputSystem` のデバイス入力へはまだ統合していない。Jump の押下値を 1〜5,000 ms だけ上書きし、同じ Actor / Session の操作に限定する。他の所有者を拒否し、単調増加時計の期限で解除する。`ReleaseSession` を切断時に所有スレッドで呼ぶ。解除後の Read は物理入力へ戻る。

`DiagnosticRequest.Permissions` / ActorId は信頼済みホストが与える前提である。今回のサンプルはローカルで実行し、サーバーへ接続しない。JSON 出力は確認用であり、Browser 通信プロトコルを実装したものではない。

[基盤 API とライフサイクル](../../src/Diagnostics/Lumyte.Diagnostics/README.md)、[ADR](../../docs/adr/diagnostics/DIAGNOSTICS-0001-diagnostics-transport.md) を参照する。
