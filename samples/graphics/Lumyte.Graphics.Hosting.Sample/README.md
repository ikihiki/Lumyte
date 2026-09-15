# 実ウィンドウ表示サンプル

同じ実行ファイルから DirectX 12、Vulkan、WebGPU（Dawn）を選び、2D → Blur → Composite → Blit → ToneMap → Output を表示します。利用側はシェーダーや descriptor を操作しません。

リポジトリのルートで実行します。

```powershell
dotnet run --project samples/graphics/Lumyte.Graphics.Hosting.Sample -- dx12
dotnet run --project samples/graphics/Lumyte.Graphics.Hosting.Sample -- vulkan
dotnet run --project samples/graphics/Lumyte.Graphics.Hosting.Sample -- webgpu
```

2 番目の引数をフレーム数にすると、自動で Host と表示を終了します。

```powershell
dotnet run --project samples/graphics/Lumyte.Graphics.Hosting.Sample -- dx12 3
```

初回 build は Native shader を Slang で生成します。利用する Slang を `LUMYTE_SLANGC` で指定できます。各 backend の必要機能は対応 ADR に従い、Vulkan のウィンドウ表示には追加の surface／swapchain maintenance1 が必要です。

application が window と message pump を所有し、DI の session から `RenderContext` を借ります。最小化中は描画を休止し、close 時は新規 frame を停止してから Host の GPU／surface 終了を待ち、最後に window を所有 thread で破棄します。ウィンドウサイズは取得時に読み、サイズ変更を Blit の出力に反映します。

基準実装は一つずつ取得・返却する frame pacing です。このサンプルは性能ベンチマークではありません。Browser canvas の実行例と画素比較は `Lumyte.Graphics.WebGPU.Browser.Tests/Integration/BrowserHost/PresentationCases.cs` にあります。
