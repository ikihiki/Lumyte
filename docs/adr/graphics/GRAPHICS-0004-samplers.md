# ADR-GRAPHICS-0004: サンプラーの所有APIと不変なsampling state

- 状態: 採用
- 日付: 2026-10-09

## 背景

TextureのstorageとViewのsubresource選択からsampling stateを独立させ、一つのsamplerを複数のtextureで共有できるようにする。backendのhandleやbinding配置を利用者へ公開せず、[Texture API](GRAPHICS-0003-textures-and-views.md)と同じ直接所有interfaceを提供する必要がある。

## 決定

### 公開API

共通型を `Lumyte.Graphics.Abstractions` に配置する。具象backendがIGraphicsSamplerを直接実装し、deviceのCreateSamplerから返す。共通所有wrapper、SamplerToken、別backend contractは設けない。device生成はbackend固有のままとする。比較元はorigin/main。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // 不正なenum・LOD・filterの組み合わせはArgumentException。
+        // capsを超えるanisotropyはNotSupportedException。暗黙補正しない。
+        // nullはArgumentNullException、解放済みdeviceはObjectDisposedException。
+        IGraphicsSampler CreateSampler(SamplerDesc desc);
     }
     public sealed record DeviceCaps
     {
+        // 有効化した機能と実装範囲を反映する。1は異方性なし。
+        public ushort MaxSamplerAnisotropy { get; init; } = 1;
     }
+    // Describes immutable sampling state without owning GPU resources.
+    public sealed record SamplerDesc
+    {
+        // Gets minification filtering.
+        public FilterMode MinFilter { get; init; } = FilterMode.Linear;
+
+        // Gets magnification filtering.
+        public FilterMode MagFilter { get; init; } = FilterMode.Linear;
+
+        // Gets interpolation between mip levels.
+        public FilterMode MipmapFilter { get; init; } = FilterMode.Linear;
+
+        // Gets addressing along the U coordinate.
+        public AddressMode AddressU { get; init; } = AddressMode.Repeat;
+
+        // Gets addressing along the V coordinate.
+        public AddressMode AddressV { get; init; } = AddressMode.Repeat;
+
+        // Gets addressing along the W coordinate.
+        public AddressMode AddressW { get; init; } = AddressMode.Repeat;
+
+        // Gets the finite nonnegative minimum LOD.
+        public float LodMinClamp { get; init; } = 0;
+
+        // Gets the finite maximum LOD, including zero when both clamps are zero.
+        public float LodMaxClamp { get; init; } = 32;
+
+        // Gets the positive anisotropic filtering limit; values above one require linear filters.
+        public ushort MaxAnisotropy { get; init; } = 1;
+
+        // Gets the comparison function, or null for ordinary sampling.
+        public CompareFunction? Compare { get; init; } = null;
+    }
+
+    // Specifies spatial or mip-level filtering.
+    public enum FilterMode
+    {
+        // Selects the nearest sample.
+        Nearest,
+
+        // Interpolates neighboring samples.
+        Linear,
+    }
+
+    // Specifies normalized coordinate addressing.
+    public enum AddressMode
+    {
+        // Clamps coordinates to the texture edge.
+        ClampToEdge,
+
+        // Repeats each coordinate interval.
+        Repeat,
+
+        // Mirrors alternating coordinate intervals.
+        MirrorRepeat,
+    }
+
+    // Specifies the comparison between the reference value and sampled depth.
+    public enum CompareFunction
+    {
+        // Never comparison.
+        Never,
+
+        // Less comparison.
+        Less,
+
+        // Equal comparison.
+        Equal,
+
+        // LessOrEqual comparison.
+        LessOrEqual,
+
+        // Greater comparison.
+        Greater,
+
+        // NotEqual comparison.
+        NotEqual,
+
+        // GreaterOrEqual comparison.
+        GreaterOrEqual,
+
+        // Always comparison.
+        Always,
+    }
+
+    // Owns a backend sampler independently of texture storage and views.
+    // The caller manages resource lifetime and access synchronization; concurrent safety is not guaranteed.
+    public interface IGraphicsSampler : IDisposable
+    {
+        // Gets the immutable sampling state used for creation, without implicit correction.
+        SamplerDesc Desc { get; }
+    }
+
 }
```

### Descと検証

Descはinit-onlyのrecordで、GPU操作を行わない。生成APIは既知のfilter・address・comparison enumを検証する。MinFilterとMagFilterは空間filter、MipmapFilterはmip間filterであり、それぞれ独立に指定する。Compareがnullなら通常sampler、設定されていれば比較samplerとする。比較samplerが使えるtexture形式とshader型は、後続binding／shaderの契約で検証する。比較samplerの確保はdepth textureの提供を意味しない。

LodMinClampとLodMaxClampは有限かつ非負で、min <= maxを必要とする。両方0は有効で、mip 0に固定する。default値への書き換え、範囲の丸め、NaN／Infinityの受け入れを行わない。

MaxAnisotropyは1以上でcaps以下。1より大きければMinFilter・MagFilter・MipmapFilterすべてLinearを必要とする。GraphicsFeatures.AnisotropicFilteringとMaxSamplerAnisotropyは有効な対応範囲を表し、非対応時は最大値1。過大な指定を上限へ丸めない。

border color、非正規化座標、LOD bias、sampler reductionは別途設計する。samplerの生成はtexture、shader、bindingの生成やGPU commandを暗黙に行わない。

### 所有と同期

Descは生成時の値を保持し、その後sampling stateは変更しない。samplerはtextureやViewを所有せず、deviceの子resourceとして所有する。利用者はsamplerを先にDisposeし、samplerが生きたdeviceのDisposeはInvalidOperationExceptionで拒否する。samplerのDisposeはidempotentで、native resourceを一度だけ解放する。native生成失敗時にdeviceのlive child数を増やさない。

APIは並列実行の安全性を保証しない。内部lock、アトミックな所有カウンター、並列処理の調停を設けず、生成・使用・解放と親子resourceの寿命に必要な同期は利用者が管理する。状態検証は競合の防止を保証しない。GPUで使われるsamplerの寿命も利用者がcommand／submissionの契約に従って保証し、DisposeはGPU完了待機を挿入しない。

## 検討した代替案

- TextureとSamplerのペアを所有するAPI: samplerを複数textureで共有できなくなるため採用しない。
- 共通Sampler classとbackend contract: 所有instanceを増やすため直接interface実装とする。
- 過大なanisotropyやLODを自動補正: 指定したsampling stateが変わるため拒否する。
- 内部cacheでsamplerを自動共有: 利用者の明示的な所有と解放を保つため、今回の契約には含めない。

## 結果と影響

利用者は共通Descでsampling stateを指定でき、backendはnative descriptorへの変換、対応機能の検証、確保と解放を実装する。native handle・比較bindingの配置・backend固有の手順は各プロジェクトのREADMEに記載する。共有やcacheは利用者がsampler instanceを保持して行える。

## 検証方針

shared sample projectから共通APIだけでdefault状態、全address／filter／comparison、LOD 0固定、不正enumと非有限LOD、anisotropy制約、寿命と二重解放を検証する。backend生成は起動部分だけで行う。Wgpu／VulkanとWasm／Browserのテストは既存CIで実行する。実際のsampling結果はshader／binding／描画APIの検証対象とする。
