# Lumyte.Graphics.Direct3D12

Windows の Direct3D 12 デバイスとバッファ転送バックエンドです。
C# の `Direct3D12Device` が `IGraphicDevice` を直接実装し、C++ の小さな C ABI を通じて
Agility SDK の Direct3D 12 と Windows SDK の DXGI を使用します。外部 binding package は不要です。

## Agility SDK の切り替え

ビルド時の `--agility-channel` で選択します。既定は `Stable` です。

| 指定 | NuGet version | SDK version |
| --- | --- | --- |
| `Stable`（一般版・既定） | `1.620.1` | 620 |
| `Preview` | `1.722.0-preview` | 722 |

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Stable
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample --agility-channel Preview
```

`Publish`、`Pack`、`Test` でも同じ引数を使用できます。通常の MSBuild では
`-p:LumyteAgilityChannel=Stable` または `Preview`、シェルで固定する場合は
`$env:LumyteAgilityChannel = 'Preview'` を使用できます。不明な値は拒否します。
切り替えるときは restore と build を実行してください。`--no-build` の実行は最後にビルドした版を使用します。
native の中間出力は SDK version ごとに分離し、出力の shim と SDK DLL は選択した版を毎回コピーします。
pack したパッケージは選択済みの版を同梱するため、利用先で channel を変えても切り替わりません。
両版のパッケージを保持する場合は `--output` で保存先を分けてください。

NuGet restore で取得した同一 package のヘッダーを CMake に渡し、対応 architecture の DLL と license を
アプリの `AgilitySDK/` へコピーします。publish と NuGet 利用時も同じ配置です。
プレビュー runtime の実行には Windows の開発者モードと、Agility SDK に対応した OS が必要です。
OS 設定はセットアップや実行時に自動変更しません。

`ID3D12SDKConfiguration1.CreateDeviceFactory` に絶対パスと、選択した版の `D3D12_SDK_VERSION` または `D3D12_PREVIEW_SDK_VERSION` を渡し、
factory から device を生成します。プロセス全体の SDK 設定や host exe の export は変更しません。
生成後の `ID3D12DeviceConfiguration.GetDesc().SDKVersion` を検証し、`SdkVersion` に保存します。
指定 runtime がない、version が一致しない、preview が許可されていない場合は失敗し、OS runtime へ fallback しません。
debug layer も同じ factory から取得します。

- [Microsoft の SDK 配布一覧](https://devblogs.microsoft.com/directx/directx12agility/)
- [Device Factory の仕様](https://microsoft.github.io/DirectX-Specs/d3d/IndependentDevices.html)
- [プレビュー runtime の開発者モード要件](https://devblogs.microsoft.com/directx/dx-dump-files-preview/)

```csharp
using var owner = Direct3D12Device.Create(new() { UseWarp = true });
IGraphicDevice device = owner;
```

`AdapterIndex` は DXGI の列挙 index です。既定は hardware の index 0 で、software adapter を拒否します。
`UseWarp = true` は WARP を明示選択し、hardware index との同時指定は拒否します。
fallback は行いません。feature level 11.0 以上の D3D12 device と direct queue を生成します。
`EnableDebugLayer` は同梱 SDK Layers と DXGI debug 機能を要求し、利用できなければ生成に失敗します。

## 対応範囲

- 型付き transfer buffer、Upload／Default／Readback heap、明示的 Map／Unmap。
- byte 単位の buffer copy、whole-buffer の明示的 transition barrier。
- one-shot command list、複数 list の一括提出、fence の状態照会とキャンセル可能な待機。
- HRESULT と fence から観測した device loss の通知。

texture、shader、pipeline、描画、swapchain、semaphore、global barrier は未実装で
`NotSupportedException` を返します。`Dxil` はバックエンドの対象形式を表し、shader compile 対応を意味しません。
Caps の未対応用途は 0、buffer の実装上限は D3D12 の保証範囲内の 128 MiB に制限しています。
copy offset／size alignment は 1。Count と stride を丸めません。

Upload は CopySource 専用で GENERIC_READ、Readback は CopyDestination 専用で COPY_DEST に固定します。
Default は COMMON から開始します。barrier は None／CopyRead／CopyWrite と、対応 heap の HostRead／HostWrite を扱います。
legacy buffer barrier は resource 全体へ作用するため、部分 range は拒否します。
buffer copy の前後状態は利用者が明示します。D3D12 の buffer state decay に従い、
ExecuteCommandLists 完了後の Default buffer は COMMON として次の提出を記録してください。
同じ提出の list は一度の ExecuteCommandLists にまとめます。

Map は GPU を待ちません。Readback は明示的な submission 完了後に読んでください。
WaitAsync は fence を非 blocking に照会し、1 ms 間隔で非同期に待機します。
キャンセルは待機のみを取り消します。Dispose は待機・生存走査を行いません。
利用者が GPU 使用完了を保証し、submission／command／buffer を device より先に解放します。
同時操作の同期と記録した native resource の寿命も利用者の責任です。
Signal が提出後に失敗した場合は command を Faulted とし、再提出を禁止します。
device loss 後の解放は `WaitForRetirementAsync` で native removal を確認してから行います。

## ビルドと検証

開発環境を有効化した native Windows PowerShell から実行します。
MSBuild hook は復元済み SDK を `--agility-sdk-root` で Fallout の `Direct3D12Native` leaf target に渡し、CMake／Ninja／MSVC で DLL を生成します。
native DLL は出力と publish へコピーし、pack は現在の SDK host RID のみを含めます。
Linux では managed assembly のコンパイルのみ可能で、device 作成は拒否し、pack の対象から外します。

```powershell
dotnet run --project build -c Release -- --target Compile --project samples/Lumyte.Graphics.Direct3D12.Sample
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- warp
dotnet run --project samples/Lumyte.Graphics.Direct3D12.Sample -c Release --no-build -- hardware
$env:LUMYTE_GRAPHICS_GPU_TESTS = '1'
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests
```

GPU テストは既存バックエンドと同じ `LUMYTE_GRAPHICS_GPU_TESTS=1` で明示的に有効化します。
未指定または Windows 以外では native GPU テストをスキップし、開発者モードを自動変更しません。

Microsoft の [heap の初期状態](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12device-createcommittedresource)と
[readback と fence の同期](https://learn.microsoft.com/en-us/windows/win32/direct3d12/readback-data-using-heaps)に従います。
