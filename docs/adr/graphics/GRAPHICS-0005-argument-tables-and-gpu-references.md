# ADR-GRAPHICS-0005: Argument Tableと型付きGPU参照

- 状態: 採用
- 日付: 2026-10-09

## 背景

shaderで使うtexture view・sampler・buffer要素を、利用者が物理binding番号やGPU addressを扱わず登録・参照できるようにする。NoGraphicsAPIのbindlessな利用形態を参考にし、複数backendで表現が違う参照を不透明な共通APIへ隠す。materialなどbuffer内容を定義するstructはapplicationに置く。

## 決定

### 登録API

機能名は「Argument Tableと型付きGPU参照」とする。Argument Tableは利用者が扱う論理的な登録先、bindingはshaderへ渡す物理配置であり、別の責務として区別する。公開契約は `Lumyte.Graphics.Abstractions`、Device生成はbackend固有のままとする。

backendの具象classがIArgumentTableを直接実装し、deviceのCreateArgumentTableから返す。TextureView、Sampler、BufferSliceを別々の論理slotへ登録し、IGpuRefを返す。slotは利用者が管理する登録位置であり、shaderのbinding番号ではない。一つのsamplerを複数textureで共有できる。

参照のTはtextureならIGraphicsTextureView、samplerならIGraphicsSampler、bufferならunmanaged要素型T。buffer参照のCountは登録rangeの要素数で、GetElement(index)はrange内の単一要素を返す。元bufferの途中のsliceを登録した場合、要素のbyte位置は `slice.OffsetInBytes + index * ElementStrideInBytes`。範囲もサイズも暗黙に補正しない。

登録tableと実際のdescriptor heap／argument table／descriptor set／bind groupは一対一に対応する必要がない。登録容量は種類ごとに独立し、同時shader bindingの上限と区別する。capacity 0はその種類の領域なし、合計は正数。Labelは診断専用。登録はCPU上のresource metadataを保持し、GPUコピーやbinding命令を発行しない。

### 公開API

比較元はorigin/main。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // capacityの合計が0ならArgumentException。nullはArgumentNullException。
+        // 解放済みdeviceはObjectDisposedException。登録容量は物理binding数ではない。
+        IArgumentTable CreateArgumentTable(ArgumentTableDesc desc);
     }
+    // /// Specifies independent logical registration capacities, not simultaneous shader binding limits.
+    ///
+    public sealed record ArgumentTableDesc
+    {
+        // Gets the optional diagnostic label.
+        public string? Label { get; init; }
+
+        // Gets the number of texture registration slots.
+        public uint TextureCapacity { get; init; }
+
+        // Gets the number of sampler registration slots.
+        public uint SamplerCapacity { get; init; }
+
+        // Gets the number of buffer registration slots.
+        public uint BufferCapacity { get; init; }
+    }
+
+    // /// Owns logical descriptor registrations; physical binding resolution consumes these registrations.
+    ///
+    // The caller manages all required lifetime and access synchronization.
+    public interface IArgumentTable : IDisposable
+    {
+        // Gets the logical texture capacity.
+        uint TextureCapacity { get; }
+
+        // Gets the logical sampler capacity.
+        uint SamplerCapacity { get; }
+
+        // Gets the logical buffer capacity.
+        uint BufferCapacity { get; }
+
+        // Registers a sampled view in a logical slot without uploading pixels.
+        // The caller-managed logical registration slot.
+        // The sampled view from this device.
+        // The opaque registration reference.
+        IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view);
+
+        // Registers a sampler independently of textures.
+        // The caller-managed logical registration slot.
+        // The immutable sampler from this device.
+        // The opaque registration reference.
+        IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler);
+
+        // Registers a shader storage range without copying its contents.
+        // The unmanaged buffer element type.
+        // The caller-managed logical registration slot.
+        // The shader range from this device.
+        // The opaque registration reference.
+        IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
+            where T : unmanaged;
+
+        // Releases an idle texture slot and invalidates its old references.
+        // The caller-managed logical registration slot.
+        void ReleaseTexture(uint slot);
+
+        // Releases an idle sampler slot and invalidates its old references.
+        // The caller-managed logical registration slot.
+        void ReleaseSampler(uint slot);
+
+        // Releases an idle buffer slot and invalidates its old references.
+        // The caller-managed logical registration slot.
+        void ReleaseBuffer(uint slot);
+    }
+
+    // Identifies registered GPU resources without exposing addresses or physical bindings.
+    // The resource or logical buffer element type.
+    public interface IGpuRef<T>
+    {
+        // Gets the number of logical elements; texture views and samplers contain one element.
+        ulong Count { get; }
+
+        // References one element relative to this registered range without copying data.
+        // The zero-based logical element index.
+        // The opaque single-element reference sharing the registration's lifetime.
+        IGpuRef<T> GetElement(ulong index);
+    }
+
 }
```

### 検証・登録identity・寿命

登録対象は同じdeviceが作った生存中の具象resourceだけとし、独自interface実装や他deviceのresourceはArgumentException。texture viewはSampled用途、bufferはShaderReadまたはShaderWrite用途を必要とする。bufferのraw rangeを登録しても、内容の数値をdescriptorとして推測しない。

slotは各capacity未満。それ以外はArgumentOutOfRangeException。空slotへのReleaseはno-op。同じslotへWriteしたら置換し、古い登録とすべての派生要素参照を失効させる。登録identityはtable・resource種別・slotの登録instanceで区別し、同じslotやresourceを再登録しても古い参照を復活させない。失効参照のGetElementはInvalidOperationException、解放済みtableの参照はObjectDisposedException。Countは不変の論理属性で、失効後も読み取れる。GetElementの範囲外はArgumentOutOfRangeException。

tableは登録中のresourceを強参照し、resourceはlive registration数を持つ。登録されたbuffer・view・samplerのDisposeはInvalidOperationExceptionで拒否する。登録されたviewがtextureを保持するため、source textureも先に解放できない。IGpuRefは非所有で、tableの登録を保持する権利を与えない。参照を持っていてもRelease／置換／table Disposeで失効する。

tableはdeviceの子resourceとして数える。tableのDisposeは全登録を失効させ、そのregistrationを解放する。登録対象のresource自体はDisposeしない。利用者はtableまたは登録を解放してからresourceをDisposeし、最後にdeviceをDisposeする。Disposeはidempotent。失敗したWriteは元slotとその参照を維持し、新しいregistrationを残さない。

内部lock、アトミックな所有カウンター、並列操作の調停は設けない。resourceとtableの生成・使用・登録の置換・解放に必要なCPU／GPU同期は利用者が管理する。live registrationの検証は競合の防止を保証しない。

### 不透明参照とbackendの追跡

IGpuRefは実address、整数、CPU pointer、descriptor indexへの変換、任意bytesからの復元を公開しない。backendは登録resource、登録identity、論理型、byte rangeと要素位置を内部で保持し、shader引数やserializerへ接続する際に検証して解決する。利用者がIGpuRefを独自実装しても有効な登録として受理しない。

IGpuRefを含む論理structはunmanagedではないため、raw `IGraphicsBuffer<T>` のCopyFromへ直接渡さない。serializerが反射情報に従ってwire bytesへpackし、論理要素とその参照metadataを関連付けるAPIはshader dataの設計で扱う。

shader dataをpackするserializerは、受け取ったIGpuRefのidentityとfieldの反射情報を記録し、「bufferのどの要素がどのresourceを必要とするか」のCPU metadataを作る。GPU bytesの整数からresourceを逆算しない。rootに単一要素参照を渡したらその要素、配列参照ならその範囲全体の候補を収集する。

直接resourceの一覧と他要素への参照を区別し、フラットな場合は直接一覧を使う。別要素への参照がある場合にだけ再帰探索・循環検査を行う。bufferのコピー・上書き・GPU書き込みによるmetadataの伝播と失効は、serializer／commandの契約で扱う。

有限bindingへ変換するbackendは、種類別に重複を排除してcompiled shaderの配置とdevice limitsへ照合する。安定した登録identityとdraw用の物理indexを分け、必要な参照変換表を構築する。一回のdrawの上限を超えた場合は拒否し、Rendererが描画単位を分ける。buffer addressやnative descriptorで直接参照できるbackendへ有限bindingの仕組みを強制しない。backend固有の表現と手順は各READMEで扱う。

## 検討した代替案

- 物理descriptor番号を共通APIで返す: backendの表現差と失効を隠せないため採用しない。
- TextureとSamplerをペアで登録する: 種類別容量とsamplerの共有を妨げるため採用しない。
- resource参照だけを返し、登録identityを持たない: slot再利用によって古い参照が復活するため採用しない。
- 共通Table classとbackend contract: 直接interface実装に揃え、所有wrapperを増やさない。

## 結果と影響

利用者は論理slotへresourceを登録し、bufferの要素参照を取得できる。backendは登録の有効性とresourceの寿命を管理し、GPU表現は後続のshader／commandとの接続で解決する。登録・参照追跡の基盤と物理bindingの生成を分離できる一方、GPU data内の参照を解決するにはserializerとshader反射・root引数の契約が必要となる。

## 検証方針

共通APIだけを使うshared sampleで、独立slot、table間同一slot、単一buffer要素、範囲、用途、slot置換・解放・再登録による参照失効、resourceとtableの寿命を検証する。Wgpu／VulkanとWasm／Browserを既存CIで検証する。shader dataのpackと物理bindingを使った描画はshader／command接続の検証対象とする。
