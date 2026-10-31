# Diagnostics経由のUI操作サンプル

既存診断サーバー、ゲームAgent、このサンプル用の型付きOperatorを別プロセスで動かします。ゲームはUIを一つ公開し、専用モードとして外部の物理入力をUIへ転送しません。bounded queueでownerへ戻し、通常frameと通常Dispatchで処理します。

```sh
# 認証tokenを環境変数へ設定し、既存サーバーを起動する。
dotnet run --project src/Diagnostics/Lumyte.Diagnostics.Server -c Release
# 別terminal。game tokenはサーバーと同じ値を設定済みとする。
dotnet run --project samples/Lumyte.Diagnostics.UI.Automation.Game.Sample -c Release -- http http://127.0.0.1:5000 none
# MagicOnionの場合は magiconion http://127.0.0.1:5001 none
# GPU readbackを公開する場合は末尾を vulkan または wgpu にする。
```

[サーバーの設定](../../src/Diagnostics/Lumyte.Diagnostics.Server/README.md)に従って、別のGameToken／OperatorTokenを使います。tokenをURLやlogへ含めません。GPUの場合は受付前にpipelineを準備し、公開後のJob期限を延長しません。

```sh
dotnet run --project samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample -c Release -- http://127.0.0.1:5000 /tmp/ui-remote.png
```

Operatorは対象発見、control取得、Click、成功indicatorのExpect、Enter、件数確認、許可されていればPNG取得、control解放を行います。PNGは対象surfaceの新しい実描画・GPU完了・readbackから取得します。別Window、装飾、他のHosted surfaceは含みません。

## 自動確認

server、game、operatorをReleaseでbuildした後に実行します。必要なSlang・SDK・GPU driverは通常のUIサンプルと同じです。

```sh
python3 tools/diagnostics/ui-automation-smoke.py
python3 tools/diagnostics/ui-automation-smoke.py --gpu vulkan
python3 tools/diagnostics/ui-automation-smoke.py --gpu wgpu
```

各実行でHTTP／MagicOnionを確認し、process ID、保存回数2回、終了処理を記録します。GPUでは分割取得したPNGのCRC、240×160寸法、成功indicatorの64画素も検査します。tokenは実行時生成し保存しません。

2026-10-29に両transport、GPUなし、Vulkan／wgpu（Mesa lavapipe）の全経路が成功しました。BrowserのHTTP Agentも別途確認しています。実機GPUロスト、Native AOT、最適化したWasm native buildは未検証です。終了はBeginStop後にframeを進め、実際のJob・host清掃を待ってからUIとGPUを返します。使用中GPUを待機取消だけで解放する処理はありません。
