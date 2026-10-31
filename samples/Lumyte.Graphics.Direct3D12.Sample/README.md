# Direct3D 12 サンプル

Direct3D 12 / WARP を生成し、共通の `GraphicsExercises` を実行する host です。
通常は `caps`、GPU 転送は `commands`、全契約検証は `suite`、三角形の表示は `window` を選びます。
backend / exercise が不足すると Spectre.Console で選択します。非対話実行では両方を指定してください。
入口は [共通ランチャー](../README.md) の `graphics` / `graphics-window` です。
Agility SDK は一般版 `1.620.1`（SDK 620）が既定です。ビルド引数 `--agility-channel Preview` で
`1.722.0-preview`（SDK 722）へ切り替えられます。Preview の実行には Windows の開発者モードが必要です。
結果には実際の device の SDK version を表示します。

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Stable
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- warp commands
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware commands
```

プレビュー版へ切り替える場合は、上のビルドコマンドの `Stable` を `Preview` に変更して再ビルドします。
`--no-build` での実行は最後にビルドした版を使用します。

Windows、MSVC、Windows SDK が必要です。[バックエンドの対応範囲](../../src/Graphics/Lumyte.Graphics.Direct3D12/README.md)も参照してください。

## ウィンドウ表示

一般版 SDK でビルドした後、`window` を指定するとネイティブウィンドウにカラー三角形を描画します。
ウィンドウのリサイズに合わせて swapchain を再構成し、閉じると GPU 資源と HWND を順に解放します。

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Stable
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware window
```

`warp window` で WARP を明示選択できます。デバッグレイヤーを有効にし、Present 成功数を標準出力に記録します。
この環境では一般版 SDK 620・実 GPU による三角形表示と連続 Present を確認しました。
Preview の部分プログラムの実行確認とは独立した結果です。

## Vulkan 共通 exercise

`suite` を付けると Vulkan と同じ command、pipeline、shader binding、depth/stencil、間接 draw／dispatch、semaphore、render graph の検証を実行します。
描画先はオフスクリーン texture で、readback の画素・計算結果を照合します。

```powershell
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware suite
```

Preview 専用の PSO 分離は、Preview で再ビルドした後に `partial` で明示します。
対応ドライバーと開発者モードが必要です。非対応環境では通常 PSO へ切り替えず、エラーになります。

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Preview
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware suite partial
```

一般版の window モードは実 GPU で表示確認済みです。Stable の WARP（debug layer 有効）では suite と同じ共通 exercise 全体もテスト済みです。Preview の部分プログラムは未実行です。
画面表示の接続は `Direct3D12Device.CreateSurface(hwnd)` を使用します。既存 HWND を渡し、window と message loop は呼び出し元で管理してください。
