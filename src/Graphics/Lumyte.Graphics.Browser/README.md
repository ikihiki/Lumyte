# Lumyte.Graphics.Browser

.NET 10 の WebAssembly と JavaScript 相互運用で、ブラウザーの WebGPU device を生成します。native graphics binding は使いません。WebGPU に対応するブラウザーと、HTTPS または localhost の secure context が必要です。

## 生成と所有権

パッケージの `wwwroot/lumyte-graphics.js` をホストが配信し、その URL を指定します。相対 URL はホストの `location.href` を基準に解決します。ビルド時は library の出力 `wwwroot/` にコピーされ、NuGet package では `contentFiles/any/net10.0/wwwroot/` に含まれます。ホスト側で実際の配信ディレクトリへ配置してください。

```csharp
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;

using BrowserDevice owner = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
IGraphicDevice device = owner;
Console.WriteLine(device.Caps.MaxBufferSize);
```

`BrowserDevice.CreateAsync(string moduleUrl, CancellationToken cancellationToken = default)` は module を import し、`navigator.gpu.requestAdapter()` と `adapter.requestDevice()` を実行します。default adapter と default device limits を使い、optional feature や高い limits は要求しません。canvas や surface は作りません。

WebGPU がない、adapter がない、device request が失敗した場合は JavaScript 相互運用から例外を伝播します。module の読み込み失敗も例外になります。ブラウザーの JavaScript 相互運用が利用できるスレッドで使用してください。

CancellationToken は module import と、生成済み device を保持するかどうかを制御します。WebGPU の adapter／device request 自体には abort API がないため、request 中のキャンセルは完了を待ち、生成した device を破棄してからキャンセル例外を返します。

所有者の `Dispose()` は `GPUDevice.destroy()` を呼んで JavaScript proxy を解放します。再度の呼び出しは何もしません。並行した解放はサポートしません。`Caps` は非所有の保存済み情報なので解放後も読み取れます。

## Caps の対応

生成した `GPUDevice.limits` を一度読み、[Wgpu の対応表](../Lumyte.Graphics.Wgpu/README.md#caps-の対応) と同じ WebGPU limits の意味で `DeviceCaps` に保存します。adapter の上限をそのまま返しません。System.Text.Json の source-generated context で読み込み、WebAssembly の trimming に対応します。

GPU buffer copy の offset／length alignment は 4 byte、encoded image copy の bytesPerRow alignment は 256 byte、storage binding の offset alignment は device の `minStorageBufferOffsetAlignment` です。format ごとの texel block 制約は別途必要です。

`IndirectDraw` と `AnisotropicFiltering` は WebGPU の基本機能として返します。非ゼロ firstInstance の optional feature は保証しません。`DepthBiasClamp` と `MeshShader` は返しません。

このプロジェクトのデバイス API は生成・解放と Caps 取得を扱います。[共通契約](../Lumyte.Graphics.Abstractions/README.md) と [.NET WebAssembly サンプル](../../../samples/Lumyte.Graphics.Browser.Sample/README.md) を参照してください。
