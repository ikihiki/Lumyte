# ADR-GRAPHICS-0003: テクスチャとサブリソースViewの所有API

- 状態: 採用
- 日付: 2026-10-09

## 背景

画像の確保と、shaderやattachmentに渡すmip・layer範囲の選択を分ける必要がある。Bufferと同じく、バックエンドの具象クラスが資源を所有する共通interfaceを提供する。バッファ内容やmaterial専用の構造体は定義しない。

## 決定

### 範囲と責務

公開APIを `Lumyte.Graphics.Abstractions` に配置する。2D、単一sampleのcolor textureを対象とし、mipと配列layer、およびD2・D2Array・Cube・CubeArray viewを提供する。各backendは `IGraphicsTexture` と `IGraphicsTextureView` を直接実装し、一つの具象instanceでnative resourceと所有状態を管理する。個別backend contract、token、所有facadeは置かない。device生成はbackend固有のままとする。

GPUコピー・barrier・submitはcommandの責務として別に設計する。textureにCPU mapping、暗黙のupload、ReadTextureAsync、自動mip生成を設けない。利用者は明示的なGPUコピーと完了待機の後、[Buffer API](GRAPHICS-0002-typed-buffers.md)のMapAsyncとCopyToでstaging bytesを読む。

### 公開API

比較元はorigin/main。宣言コメントは契約を表し、backendの実装手順は各プロジェクトのREADMEで扱う。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // 正の寸法・mip・layer、既知のformatと非空usageを検証して確保する。
+        // 解放済みdeviceはObjectDisposedException。不正DescはArgumentException。
+        // 未対応format／usageや物理制約はNotSupportedException。勝手な置換・縮小はしない。
+        IGraphicsTexture CreateTexture(TextureDesc desc);
     }
     public sealed record DeviceCaps
     {
+        // 2D texture配列の最大layer数。空間寸法はMaxTextureDimension2D。
+        public uint MaxTextureArrayLayers { get; init; }
     }
+    // Describes a single-sample two-dimensional texture with mip levels and array layers.
+    public sealed record TextureDesc
+    {
+        // Gets the positive base width in texels.
+        public required uint Width { get; init; }
+
+        // Gets the positive base height in texels.
+        public required uint Height { get; init; }
+
+        // Gets the exact storage format; no implicit substitution is permitted.
+        public required TextureFormat Format { get; init; }
+
+        // Gets the nonempty set of permitted operations.
+        public required TextureUsage Usage { get; init; }
+
+        // Gets the number of allocated mip levels; content is not generated automatically.
+        public uint MipLevels { get; init; } = 1;
+
+        // Gets the positive array layer count, unchanged across mip levels.
+        public uint ArrayLayers { get; init; } = 1;
+    }
+
+    // Specifies color storage formats without guaranteeing support for every device usage.
+    public enum TextureFormat
+    {
+        // Four normalized linear eight-bit channels.
+        Rgba8Unorm,
+
+        // sRGB color channels and linear eight-bit alpha.
+        Rgba8Srgb,
+
+        // Normalized linear BGRA eight-bit channels.
+        Bgra8Unorm,
+
+        // sRGB BGRA color and linear eight-bit alpha.
+        Bgra8Srgb,
+    }
+
+    // Specifies permitted texture operations.
+    [Flags]
+    public enum TextureUsage
+    {
+        // Allows GPU copies to read the image.
+        CopySource = 1,
+
+        // Allows GPU copies to write the image.
+        CopyDestination = 2,
+
+        // Allows shader sampling through views.
+        Sampled = 4,
+
+        // Allows use as a render attachment.
+        RenderAttachment = 8,
+    }
+
+    // Specifies how shaders interpret selected image layers.
+    public enum TextureViewDimension
+    {
+        // One two-dimensional layer.
+        D2,
+
+        // A two-dimensional layer array.
+        D2Array,
+
+        // Six square faces ordered +X, -X, +Y, -Y, +Z, -Z.
+        Cube,
+
+        // One or more consecutive cubes with six layers each.
+        CubeArray,
+    }
+
+    // Selects a view dimension and mip and layer ranges without allocating resources.
+    public sealed record TextureViewDesc
+    {
+        // Gets the view dimension; cube views require square faces.
+        public TextureViewDimension Dimension { get; init; } = TextureViewDimension.D2;
+
+        // Gets the first mip level.
+        public uint BaseMipLevel { get; init; }
+
+        // Gets the positive mip count, or null for all remaining levels.
+        public uint? MipLevelCount { get; init; }
+
+        // Gets the first array layer.
+        public uint BaseArrayLayer { get; init; }
+
+        // Gets the positive layer count, or null for the dimension-specific default.
+        public uint? ArrayLayerCount { get; init; }
+    }
+
+    // Contains immutable, normalized view attributes; counts never contain sentinels.
+    public sealed record TextureViewInfo
+    {
+        // Gets the source storage format.
+        public required TextureFormat Format { get; init; }
+
+        // Gets the resolved view dimension.
+        public required TextureViewDimension Dimension { get; init; }
+
+        // Gets the first selected mip.
+        public required uint BaseMipLevel { get; init; }
+
+        // Gets the positive resolved mip count.
+        public required uint MipLevelCount { get; init; }
+
+        // Gets the first selected array layer.
+        public required uint BaseArrayLayer { get; init; }
+
+        // Gets the positive resolved layer count.
+        public required uint ArrayLayerCount { get; init; }
+    }
+
+    // Owns one backend texture allocation; dispose views before disposing their texture.
+    public interface IGraphicsTexture : IDisposable
+    {
+        // Gets the base width in texels.
+        uint Width { get; }
+
+        // Gets the base height in texels.
+        uint Height { get; }
+
+        // Gets the allocated mip count.
+        uint MipLevels { get; }
+
+        // Gets the immutable array layer count.
+        uint ArrayLayers { get; }
+
+        // Gets the exact storage format.
+        TextureFormat Format { get; }
+
+        // Gets the immutable permitted usages.
+        TextureUsage Usage { get; }
+
+        // 範囲内mipの正の幅・高さを返す。layer数は変わらない。
+        // 範囲外はArgumentOutOfRangeException、解放済みはObjectDisposedException。
+        (uint Width, uint Height) GetMipSize(uint mipLevel);
+
+        // Creates an owned view in the source format; null selects the full default view.
+        // The requested ranges and dimension, or null for D2 or D2Array according to layer count.
+        // The backend view retaining its texture until disposal.
+        IGraphicsTextureView CreateView(TextureViewDesc? desc = null);
+    }
+
+    // Owns a backend texture view and retains its source allocation.
+    public interface IGraphicsTextureView : IDisposable
+    {
+        // Gets the retained source texture; disposal is refused while views remain alive.
+        IGraphicsTexture Texture { get; }
+
+        // Gets the normalized dimension, format and subresource range.
+        TextureViewInfo Info { get; }
+    }
+
 }
```

### 寸法とsubresourceの検証

Width、Height、ArrayLayers、MipLevelsは正数。Width／HeightとArrayLayersはdevice caps以下で、MipLevelsは `floor(log2(max(Width, Height))) + 1` 以下。mip寸法は `max(1, base >> mip)`、array layer数はmipによらず一定。Descはinit-onlyで、生成後の属性は不変。formatは列挙された正確なstorage formatを使い、sRGBをlinearへ暗黙に置き換えない。enumの存在はすべてのbackendで全用途が使える保証ではない。

Viewはsourceと同じformatのみ。SampledまたはRenderAttachment用途を必要とし、copy専用textureのCreateViewはInvalidOperationException。baseは範囲内、明示countは正数で `count <= total - base` を満たす。null mip countは残り全mip。null layer countはD2なら1、Cubeなら6、D2Array／CubeArrayなら残り全layer。D2のcountは1、Cubeは6、CubeArrayは6の正の倍数。cubeのbase layerに6の倍数制約は設けず、選択した連続6枚を一つのcubeとして解釈する。Cube系はsourceのWidthとHeightが等しいことを必要とする。null Descはlayer数1ならD2、複数ならD2Arrayの全範囲を選ぶ。

color aspectは常に全channel。view format再解釈、depth／stencil、storage texture、圧縮format、MSAA、1D／3Dは後続の設計対象とする。

### 所有と解放

利用者はView、Texture、Deviceの順でDisposeする。Viewはsourceを強参照し、textureのlive view数を保持する。ViewがあるTextureのDispose、およびtextureまたはbufferが残るDeviceのDisposeはInvalidOperationExceptionで拒否し、資源を解放しない。解放はidempotent。解放済みTextureのCreateViewはObjectDisposedException。生存中ViewのTextureは解放されない。

APIは並列実行の安全性を保証せず、lock、アトミックな所有カウンター、並列操作の調停を実装しない。backendが許す並列実行の範囲、同じresourceの生成・CPUアクセス・map／unmap・解放、および親子resourceの生成と解放の競合に必要な同期は利用者が管理する。live child数とmapping状態の検証は、利用者が必要な同期を行った後の操作に対する契約であり、競合の検出・防止を保証しない。この方針はdevice、buffer、texture、viewに共通して適用する。GPUが使用中の資源の解放やsubresourceへのアクセスを安全にする同期は、利用者がcommand／submissionの契約に従って保証する。DisposeはGPU命令や完了待機を挿入しない。native view生成に失敗した場合、view数と資源の所有状態を変更しない。

## 検討した代替案

- 共通Texture classとbackend contract: 所有wrapperを増やすため採用しない。
- textureを直接shaderに渡す: 選択するmip・layerとdimensionが曖昧になるため、明示Viewに分ける。
- Disposeを遅延解放として受け付ける: 所有関係と失敗時の状態を明確にするため、生存する子を先に解放する契約とする。
- textureへupload／readbackを自動化する: commandとCPUコピーの責務を混ぜるため採用しない。

## 結果と影響

利用側はbackendのhandleやdescriptor配置を扱わず、同じAPIでallocationとsubresource viewを作成できる。バックエンドはformat・usage・物理寸法への対応を検証し、native imageとviewの確保・解放および失敗時cleanupを実装する。転送や描画が加わるまで、このADRの検証対象はallocation・View属性・範囲・所有の契約となる。

## 検証方針

共通APIを使うshared sampleを用意し、mip・layerの正規化、cube条件、不正範囲、format・usage検証、View／Texture／Deviceの寿命を確認する。各backendの生成は起動部分だけで行う。Wgpu・VulkanとWasm／Browserのテストは既存CIで実行する。
