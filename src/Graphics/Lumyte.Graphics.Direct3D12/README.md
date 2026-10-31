# Lumyte.Graphics.Direct3D12

Windows の Direct3D 12 グラフィックスバックエンドです。
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
fallback は行いません。feature level 11.0 以上と Enhanced Barriers を要求し、direct queue を生成します。
`EnableDebugLayer` は同梱 SDK Layers と DXGI debug 機能を要求し、利用できなければ生成に失敗します。

## 対応範囲

Vulkan と同じ `IGraphicDevice`／command／encoder API を実装します。

| 機能 | Direct3D 12 実装 |
| --- | --- |
| Buffer | Default／Upload／Readback、型付き・shader data、map、copy、Index／Indirect／SRV／UAV |
| Texture | 2D、mip、array、color 10 形式、Depth32Float／Depth24Stencil8、view、明示的 copy |
| Sampler／Argument Table | 論理 slot と参照、draw／dispatch ごとの immutable descriptor heap |
| Shader | Slang DXIL SM 6.6、root 定数、参照を含む shader data、オンライン／オフライン共通 ABI |
| Graphics | clear／load／store、depth／stencil、blend、viewport／scissor、indexed／indirect draw |
| Compute | root 引数、dispatch／indirect dispatch、GPU 書き込み |
| Synchronization | Enhanced global／buffer range／texture subresource barrier、binary semaphore、submission fence |
| Presentation | 借用 HWND、DXGI flip-discard、acquire／present／resize、frame lease |

buffer の実装上限は 128 MiB、copy offset／size alignment は 1 です。Count と stride は丸めません。
texture buffer copy は offset 512 byte、row pitch 256 byte alignment を要求します。
array copy は各 layer の先頭も 512 byte alignment を満たす必要があります。
color format は RGBA8／BGRA8 Unorm・sRGB、R8／RG8 Unorm、R16／RG16／RGBA16 Float、RGB10A2 Unorm。
depth/stencil は attachment 用途のみ、sample count は 1 です。

Shader root signature は Resource Binding Tier 2 以上と SM 6.6 を要求します。
texture 128 slot、sampler 16 slot、buffer 64 slot を設けます。参照は 16 byte の slot／byte offset／count／reserved です。
DXIL の root は `b8, space0`、texture は `t0, space0`、sampler は `s0, space0`、
raw SRV／UAV buffer は `t0/u0, space1`。shader data の stride は Slang の natural layout、
root は constant-buffer layout を reflection から取得し、同じレイアウトだと仮定しません。
GPU へ渡す root と descriptor は command が保持し、以降の SetArguments や table 変更で書き換えません。
GPU 書き込み buffer は UAV を作成し、readonly buffer は SRV を使用します。
helper の texture sampling は単一 layer の Texture2D のみです。array／cube view は作成できますが、helper への binding は拒否します。

## PSO と Preview の分離

共通 API の shader program と draw state の分離は両 channel で使用できます。
既定では完全な native PSO を state／attachment format ごとに生成して cache します。
viewport、scissor、blend constant、stencil reference と resource の内容は cache key に含めません。

Preview で native の Partial Graphics Programs を使う場合は明示します。

```csharp
using var device = Direct3D12Device.Create(new()
{
    EnablePartialGraphicsPrograms = true,
    EnableDebugLayer = true,
});
Console.WriteLine(device.PartialGraphicsProgramsEnabled);
```

この指定は Preview SDK、Windows 開発者モード、state-object experiment、対応ドライバーを必要とします。
Device Factory に対して experiment を有効にし、`D3D12_FEATURE_PARTIAL_GRAPHICS_PROGRAMS` の Tier 1.0 以上を確認します。
Stable や非対応ドライバーへの自動 fallback はありません。通常の PSO を使用する場合は指定を false にします。

program 作成時に VS／PS を合わせた partial collection を生成し、draw 時に rasterizer／blend／depth／attachment 等を link します。
`PreferReuse` は `PREFER_MINIMAL_LINK`、`FullSpecialization` は `FULL_SPECIALIZATION` を executable state object に指定します。
background specialization は起動しません。通常 PSO 経路ではこの最適化 hint に対応する native 制御がなく、両値で同じ cache 方針です。
link 結果も program とともに保持して等価な state で再利用します。現在の環境ではドライバー制約により Preview の実行ができないため、
link コストと cache 比較は未測定です。初回 link 時間・native shader 再コンパイル回数・性能改善を保証しません。

- [Partial Graphics Programs の仕様](https://microsoft.github.io/DirectX-Specs/d3d/PartialGraphicsPrograms.html)
- [共通の program／draw state 契約](../../../docs/adr/graphics/GRAPHICS-0008-pipeline-programs-and-render-state.md)

## 提出・寿命

Upload は GPU read 用途、Readback は CopyDestination 専用です。buffer barrier は範囲を指定でき、
Enhanced Barriers の access／sync として記録します。legacy resource state decay をアプリ側で追跡する必要はありません。
texture の layout、access、subresource は利用側が指定します。Undefined からの遷移は内容を破棄します。
同じ提出の list は一度の ExecuteCommandLists にまとめます。semaphore の待機は指定 stage を含む queue 全体を待ち、
binary signal の消費を native fence の値に対応付けます。未 signal の wait、二重 signal、同一提出の重複 semaphore は拒否します。

Map は GPU を待ちません。Readback は明示的な submission 完了後に読んでください。
WaitAsync は fence を非 blocking に照会し、1 ms 間隔で非同期に待機します。キャンセルは待機のみを取り消します。
Dispose は GPU 待機・依存 resource の生存走査を行いません。利用者が GPU 使用完了と並行操作の同期を保証し、
submission／command／pipeline／resource を device より先に解放してください。
提出後の signal 失敗では command を Faulted にし、再提出を禁止します。
device loss 後は `WaitForRetirementAsync` で native removal を確認してから解放します。

## HWND と画面表示

```csharp
using IGraphicsSurface surface = device.CreateSurface(hwnd);
using IGraphicsSwapchain swapchain = surface.CreateSwapchain(new()
{
    Width = 640,
    Height = 480,
    Format = TextureFormat.Bgra8Unorm,
});
```

window の作成、message loop、寿命はアプリが管理します。surface は HWND を破棄しません。
Fifo を使用でき、DXGI tearing 対応時に Immediate も使用できます。alpha は Auto／Opaque、format は
RGBA8／BGRA8 Unorm、RGBA16 Float、RGB10A2 Unorm に限定します。要求値の暗黙の変更はしません。
各 swapchain は一度に一つの frame lease を貸し出します。GPU 提出と present に渡す semaphore は利用側が明示します。
frame の texture は借用で、texture 単独の Dispose は拒否します。
WaitForReleaseAsync は present 後の queue fence を待ち、display scanout の完了を意味しません。
GPU 使用完了と release 待機の後に frame／view を破棄してから再取得・resize してください。
未提出 frame の破棄も可能です。HWND の失効、device removal、DXGI の失敗は例外として通知します。

## ビルドと検証

開発環境を有効化した native Windows PowerShell で実行します。
MSBuild hook が復元済み SDK を Fallout の Direct3D12Native target に渡し、CMake／Ninja／MSVC で DLL を生成します。
pack は現在の Windows SDK host RID のみを含みます。Linux は managed assembly のコンパイルのみで、device 生成・pack は非対応です。

```powershell
# GPU を起動しない検証。Preview でも同様に実行可能。
$env:LUMYTE_GRAPHICS_GPU_TESTS = '0'
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --agility-channel Stable
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --agility-channel Preview

# 対応環境でのみ有効化する GPU 検証。
$env:LUMYTE_GRAPHICS_GPU_TESTS = '1'
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --agility-channel Stable
# Preview の partial program テストは別途明示する。
$env:LUMYTE_D3D12_PARTIAL_TESTS = '1'
dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --agility-channel Preview
```

GPU を使わないテストは DXIL container、helper の binding、natural／constant-buffer layout、オンライン／オフライン一致、
stage 間の schema 不整合を確認します。GPU テストは Vulkan と同じ Buffer／Texture／Sampler／ArgumentTable／Shader／Command／Pipeline／ShaderBinding／AdvancedCommand／Semaphore／RenderGraph exercise を使用します。
一般版 SDK 620 の window サンプルは実 GPU で三角形表示と連続 Present を確認済みです。両 channel のビルドと CPU テスト、およびこの表示確認と、
未実施の suite 全体・WARP・Preview partial program の実行検証を区別してください。
