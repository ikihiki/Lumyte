# ADR-0011: サンプラーの利用 API とバックエンド実装契約

- 状態: 提案
- 日付: 2026-10-07

## 背景

SamplerDesc のフィールドだけでは、通常／比較 sampler の型、filterable format との組合せ、Slang reflection に基づく引数構築、descriptor の寿命が確定しない。Texture に sampler を埋め込むと、同じ画像を異なる sampling 条件で使う際に resource と pipeline の管理が複雑になる。

本 ADR を SamplerDesc の正本とし、Sampler を [ADR-0010](0010-texture-resource-contract.md) の TextureView とは独立した所有 resource として設計する。[ADR-0005](0005-shader-compilation-and-data-interop.md) の生成引数型と BindingPlan を通じて組み合わせる。現在の初期 wgpu 実装には Sampler がなく、以下は新しい共通契約の提案である。

## 決定

### 利用側の公開 API

型は `Lumyte.Graphics` 名前空間。Sampler は sealed な immutable 所有 class とする。利用者による constructor、sampling state の変更、native descriptor／binding index の取得は提供しない。状態を変える場合は別の Sampler を生成する。

API は .NET の API review／API diff に倣い、namespace・型・メンバーを C# 宣言でまとめる。`+` は origin/main に対する追加 API、`-` は削除 API、無印は変更の文脈を表す。この PR の main には Graphics API がないため、掲載する宣言は追加として表示する。各ブロックは当該 ADR の対象メンバーの抜粋であり、実装コードではない。説明と検証条件は宣言の `//` コメントに記す。提案と実装済みの区別は ADR の状態と本文に従う。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice : IDisposable
+    {
+        // enum／数値／capability を検証し、Desc を snapshot して生成
+        // 対応する完全な設定を実現できる場合だけ成功
+        public Result<Sampler> CreateSampler(SamplerDesc desc);
+    }
+
+    public sealed class Sampler : IDisposable
+    {
+        // 確定した sampling 設定と Kind を返す immutable record
+        // GPU handle は含まない
+        public SamplerInfo Info { get; }
+
+        // idle 時に内部参照を解放
+        // idempotent
+        // 引数・記録・GPU 使用で lease されていれば InvalidOperationException
+        public void Dispose();
+    }
+
+    public sealed record SamplerInfo
+    {
+        // NonFiltering／Filtering／Comparison
+        // 下記ルールで設定から決まる
+        public SamplerKind Kind { get; }
+    }
+
+    public sealed class DeviceCaps
+    {
+        // shader stage ごとの同時 sampler binding 上限
+        // BindingPlan の samplers と texture／buffer を含む総 binding 上限も検証
+        public uint MaxSamplersPerStage { get; }
+
+        // 実装が保証する上限、最低 1
+        // > 1 の要求には AnisotropicFiltering feature も必要
+        public uint MaxAnisotropy { get; }
+    }
+}
+
+namespace Lumyte.Graphics.Runtime
+{
+    public sealed class FrameContext
+    {
+        // 生成 C# 型の Sampler と TextureView field を pack
+        // reflection と両者の互換性、所属、寿命を検証
+        public ShaderArguments CreateArguments<T>(ShaderArgumentsLayout<T> layout, in T values) where T : IShaderArgumentsData;
+    }
+}
```

```diff
+namespace Lumyte.Graphics
+{
+    // SamplerInfo は MinFilter／MagFilter／MipFilter、AddressU／V／W、MinLod／MaxLod、MaxAnisotropy、Compare と Kind を持つ
+    // Label は互換性と cache key に含めない
+    // 変更可能な Desc を getter で返さない
+    // 引数を構築した後に別の sampler を使うには新しい ShaderArguments を構築する
+    public sealed record SamplerInfo
+    {
+        public Filter MinFilter { get; }
+        public Filter MagFilter { get; }
+        public Filter MipFilter { get; }
+        public AddressMode AddressU { get; }
+        public AddressMode AddressV { get; }
+        public AddressMode AddressW { get; }
+        public float MinLod { get; }
+        public float MaxLod { get; }
+        public uint MaxAnisotropy { get; }
+        public CompareOp? Compare { get; }
+    }
+
+    // Kind は設定から導出する。数値表現を互換契約にしない。
+    public enum SamplerKind { NonFiltering, Filtering, Comparison }
+}
```

### SamplerDesc

Desc は `public sealed record SamplerDesc`、init-only property、`string? Label = null`。各 enum の未知値は拒否する。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record SamplerDesc
+    {
+        // Desc の構築では GPU 操作を行わない
+        // 生成・記録 API が検証する
+        public SamplerDesc();
+
+        // 診断用ラベル
+        // 動作と互換性を変えない
+        public string? Label { get; init; } = null;
+
+        // minification の texel filter
+        // Nearest／Linear
+        public Filter MinFilter { get; init; } = Filter.Nearest;
+
+        // magnification の texel filter
+        // Nearest／Linear
+        public Filter MagFilter { get; init; } = Filter.Nearest;
+
+        // mip 間の選択／補間
+        // Nearest／Linear
+        public Filter MipFilter { get; init; } = Filter.Nearest;
+
+        // ClampToEdge／Repeat／MirrorRepeat
+        // 正規化座標の各軸に適用
+        public AddressMode AddressU { get; init; } = AddressMode.ClampToEdge;
+
+        // ClampToEdge／Repeat／MirrorRepeat
+        // 正規化座標の各軸に適用
+        public AddressMode AddressV { get; init; } = AddressMode.ClampToEdge;
+
+        // ClampToEdge／Repeat／MirrorRepeat
+        // 正規化座標の各軸に適用
+        public AddressMode AddressW { get; init; } = AddressMode.ClampToEdge;
+
+        // 有限かつ ≥ 0
+        public float MinLod { get; init; } = 0;
+
+        // 有限かつ ≥ MinLod
+        // +Infinity／NaN は拒否
+        public float MaxLod { get; init; } = 32;
+
+        // 1〜Caps.MaxAnisotropy
+        // > 1 は全 filter=Linear、feature が必要
+        public uint MaxAnisotropy { get; init; } = 1;
+
+        // null は通常、指定時は比較 sampler
+        // Never／Less／Equal／LessEqual／Greater／NotEqual／GreaterEqual／Always
+        public CompareOp? Compare { get; init; } = null;
+    }
+}
```

LOD は sampler 範囲に制限した後、選択 view の利用可能 mip 範囲にも制限される。LOD 0 は view の BaseMipLevel に対応する。整数 texel Load と storage read／write には Sampler を使わない。未使用軸も Desc として検証する。

Compare != null なら Kind=Comparison。それ以外で一つでも Linear、または anisotropy > 1 なら Kind=Filtering。残りは NonFiltering。Comparison にも texel／mip filter の設定は適用するが、通常の Filtering 型として bind しない。比較結果の PCF kernel と anisotropic sampling の実装は native の保証範囲であり、backend 間の bit 一致を要求しない。

共通 API は正規化座標のみを扱い、unnormalized coordinates、border color、LOD bias、reduction min／max、static sampler、利用側指定の descriptor slot を初期契約に含めない。MaxLod や anisotropy を対応値へ黙って丸めない。要求設定を native API で正確に表現できなければ UnsupportedFeature とする。

### TextureView・Slang との適合

| shader の論理的な使用 | 必須条件 |
| --- | --- |
| 通常の float texture Sample | sampled color view、sample count 1、dimension 一致。Filtering は view format の filterable 対応が必要 |
| filterable でない float texture | NonFiltering のみ、profile の非 filtering sample 操作が対応すること。Linear の sampler を流用しない |
| integer texture | 初期の library は texel Load のみ。Sampler を伴う sampling 経路を公開しない |
| depth の通常 sample | sampled depth view と shader の depth 型、sample count 1。filter 設定は対応 capability に従う |
| depth comparison sample | Comparison、depth の sampleable aspect、sample count 1、comparison-sampleable capability。shader の比較 sampler 型と一致 |
| storage／multisampled texture | sampler は不要。texture Load／storage 専用経路を使用 |

通常 sampler と Comparison は互換でない。shader 側の型で見分けられるよう、Slang の SamplerState と SamplerComparisonState を論理スキーマに反映する。texture と sampler の参照パスの組合せを library interface／BindingPlan に保持し、引数構築時に関連 view の capability と一緒に検証する。同じ sampler を複数 texture に使う場合は各組合せを検証する。

WebGPU の非 filtering sampler は nearest の全 filter を要求する。生成 BindingPlan は non-filtering／filtering／comparison の binding category を確定し、異なる category の sampler を同じ物理 layout に後から渡せない。同じ論理 shader を別 category で使う場合は、コンパイル時の profile／特殊化を分ける。runtime が異なる category の layout を暗黙に生成して pipeline とずらさない。

texture と sampler が別 field の場合も利用者に binding 番号は指定させない。profile が組合せを静的に確定できない動的な sampler graph は診断付きで拒否する。DirectX／Vulkan の実装で descriptor indexing が使えても共通 profile の限界を越える機能を暗黙に提供しない。

### バックエンドが実装するもの

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    // 内部操作の設計用宣言。Token／Range／Plan などは非公開の概念型。
+    // 正式な driver signature、結果／診断型、C ABI の layout は別途具体化する。
+    // この表示は現行 IGraphicsDriver の実装を変更しない。
+    internal interface ISamplerBackendContract
+    {
+        // 使用可能 filter／address／compare と anisotropy 上限、shader profile 制限を報告
+        // 未実装機能を native support だけで有効にしない
+        SamplerCapabilities GetSamplerCapabilities();
+
+        // 全設定を native state に変換、所属と世代を保持
+        // 確保失敗 rollback、idle 時の解放
+        SamplerToken CreateSampler(SamplerDesc normalizedDesc);
+
+        // 全設定を native state に変換、所属と世代を保持
+        // 確保失敗 rollback、idle 時の解放
+        void DestroySampler(SamplerToken token);
+
+        // Kind、device、世代、lease を検証し、descriptor／binding へ内部変換
+        ResolvedSamplerReference ResolveSampler(SamplerToken token, SamplerKind reflectedCategory, BindingPlan bindingPlan);
+
+        // dimension、aspect、sample type、filter／compare、sample count を照合
+        // pack／記録前に拒否
+        void ValidateSamplingPair(TextureViewToken textureView, SamplerToken sampler, ReflectedSamplingUse reflectedUse);
+
+        // Native と Slang が共有する ABI に従って引数を構築
+        // slot／address／bind group を利用側に返さない
+        void PackSamplerReference(ShaderFieldPath logicalPath, SamplerToken token, BindingPlan bindingPlan);
+
+        // 引数、CommandBuffer、Submission、cache の native lifetime を管理
+        // GPU 完了前に descriptor を再利用しない
+        void RetainSampler(SamplerToken token);
+
+        // 引数、CommandBuffer、Submission、cache の native lifetime を管理
+        // GPU 完了前に descriptor を再利用しない
+        void ReleaseSampler(SamplerToken token);
+    }
+}
```

| backend | 対応方法と注意点 |
| --- | --- |
| managed wgpu | Ahjo sampler と bind group。filter／address／LOD／compare／maxAnisotropy を設定し、binding category と format feature を検証。独自 .Native は不要 |
| DirectX | sampler descriptor と必要な descriptor heap。filter／comparison／anisotropic enum への変換、descriptor 再利用の fence 管理。初期経路で static sampler を要求しない |
| Vulkan | VkSampler、samplerAnisotropy feature と maxSamplerAnisotropy、descriptor の種類と lifetime。combined image sampler が内部で必要でも公開 resource は分離 |
| Browser WebGPU | GPUSampler と bind group。promise／error scope の失敗を GraphicsError／Submission へ伝える。ホストの Native に依存しない |

DirectX／Vulkan の C ABI は enum／bool を uint32、LOD を float32、resource を opaque token で渡す。C# bool の実メモリ配置へ依存しない。BindingPlan が sampler と texture の field path を関連付け、Native が対象 token と reflected category を照合する。sampler descriptor の数値や Native pointer を生成 C# 引数に保存しない。完全な ABI POD 宣言は別途定める。

### 共有、キャッシュ、寿命

Sampler は texture に依存しない。通常／比較設定が適合する複数 view と共有できる。ShaderArguments の保持期間とその GPU 使用期間には Sampler を lease する。FrameContext の引数なら当該 frame の Submission 完了まで保持し、再利用前に失効させる。

backend は同一 normalizedDesc の native sampler を cache してよい。公開 Sampler は別の所有 wrapper を返し、ReferenceEquals、native の同一性、生成数を保証しない。各 wrapper の Dispose を独立に処理し、共有 native sampler は参照数と GPU 使用の両方が zero の時だけ回収する。cache は Device ごとで、上限と eviction を設けて無制限に増やさない。Label が異なる場合も cache 共有を許すが、診断には公開 wrapper の Label を残す。

Sampler 自体に GPU 書き込みや resource barrier はない。texture のアクセス遷移は TextureView と ADR-0010 に従う。sampler の descriptor を保持するために dummy texture や CPU の完了待機を作らない。FrameContext と Encoder は単一スレッド、生成・解放は Device の直列化規約に従う。

null／enum／LOD／anisotropy 範囲・別 Device・型不適合は引数例外、失効は ObjectDisposedException、lease 中解放は InvalidOperationException。有効な設定の未対応機能、確保失敗、DeviceLost は Result の GraphicsError。pack 中の型不適合は native に渡す前に拒否し、native 記録失敗と非同期 validation error は ADR-0006 の Faulted／Submission error 契約に従う。

### 利用例

以下は提案 API。生成 Result を確認して得た `linearSampler`、`nearestSampler`、`depthComparisonSampler` を使用する。

```csharp
// 同じ albedoView を異なる sampling で使用できる。
var smooth = frame.CreateArguments(materialLayout, new MaterialArguments {
    Albedo = albedoView, Sampling = linearSampler,
});
var pixelated = frame.CreateArguments(nonFilteringMaterialLayout, new MaterialArguments {
    Albedo = albedoView, Sampling = nearestSampler,
});
// 各 argumentsLayout に適合する pipeline を選ぶ。
var shadow = frame.CreateArguments(shadowLayout, new ShadowArguments {
    Depth = depthView, Comparison = depthComparisonSampler,
});
```

## 検討した代替案

### sampler を texture に固定する

画像 memory の共有と sampling 条件の変更を分離できない。独立 resource として引数で関連付ける。

### 通常／比較／filtering の違いを backend 任せにする

WebGPU の binding layout と shader 型で validation error になり、backend ごとに動作が変わる。reflection と capability を使って共通層で適合を検証する。

### Sampler の状態を in-place で変更する

記録済みコマンドの意味と descriptor の寿命が不安定になる。immutable state と新規生成を使い、native cache で生成コストを抑える。

## 結果と影響

- 同じ texture に複数 sampling 条件を使え、共通引数 API に物理 binding を露出しない。
- Slang の pair metadata と sampler category ごとの layout 検証が必要になる。
- native cache の参照数・GPU 完了・診断 label の管理が必要になる。
- PCF／anisotropy の画質と数値は backend ごとに差があり、全 pixel の bit 一致は保証しない。
- 今回の変更は設計文書のみ。Sampler の実装と GPU 検証は未実施。

## 検証方針

共通 API だけを使い、nearest／linear、Repeat／MirrorRepeat／ClampToEdge、mip LOD、比較 shadow を画像 Readback で検証する。境界近傍の filter 結果は許容誤差を設け、対応しない anisotropy、NaN／Infinity、不明 enum、comparison／通常の混同、integer／unfilterable texture と Linear の不適合を拒否する。backend の cache 共有時も別 wrapper の Dispose、in-flight descriptor の非再利用、DeviceLost と失敗 rollback を確認する。

## 別途決定する事項

- cache の容量・eviction 方針、descriptor budget と計測指標。
- border color、LOD bias、reduction sampler、static／immutable sampler の最適化と専用 profile。

## 参考資料

- [Slang と GPU データ受け渡し](0005-shader-compilation-and-data-interop.md)
- [テクスチャ・ビュー](0010-texture-resource-contract.md)
- [WebGPU samplers](https://www.w3.org/TR/webgpu/#samplers)
