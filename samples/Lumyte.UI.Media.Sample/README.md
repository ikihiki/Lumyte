# Media headless scenario

公開Composition factoryを標準Headless hostから実行し、実フォントの文字組み、画像未供給時の占有寸法、
vector icon、Signal進捗、借用ManualClockで動くBusyIndicatorを記録します。
フォントはアプリが明示供給し、OSフォントを暗黙検索しません。

```sh
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.UI.Media.Sample
dotnet run --project samples/Lumyte.UI.Media.Sample -c Release --no-build -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
```

solutionをCompileした場合は共通runnerから検査と実行をまとめられます。

```sh
dotnet run --project build -c Release -- --target TestUiMedia --no-build --no-restore
```

既定の画像は未供給placeholderです。準備済み画像の供給例とGPU完了までの所有者保持は
[Media README](../../src/UI/Lumyte.UI.Media/README.md#画像とgpu寿命)を参照してください。
このscenarioはGPUを作らず、Canvasへの実文字・icon・進捗命令の記録を確認します。
画像GPU統合試験とは区別します。

## 確認範囲

Linux x64、SDK 10.0.403でmanaged buildと実行、ILLink/ILC warnings-as-errorsのNativeAOT publishと
生成ELFの実行を確認しました。実フォントは同梱テストfixtureのDejaVuSansを明示供給しています。
画像のupload、差替え・unmount後のsnapshot、GPU完了待ちとretained/direct画素一致は
Graphics.TestsのUiMediaLifetimeTestsでVulkan/wgpuのsoftware Lavapipeにより確認しました。
Group alphaはDrawing2Dのcommand境界検査であり、追加effect passの画素試験ではありません。
Windows、Browser、hardware GPUでのMedia実行は未検証です。
