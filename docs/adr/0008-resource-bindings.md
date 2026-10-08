# ADR-0008: Argument Table と型付き GPU 参照の追跡・binding 解決

- 状態: 採用
- 日付: 2026-10-07
- 更新日: 2026-10-08

## 背景

NoGraphicsAPI のように resource descriptor を登録し、参照を GPU data に格納する共通 API を設計する。利用者は buffer の特定要素、texture view、sampler を不透明な型付き参照で扱い、物理 descriptor index や binding 番号を指定しない。Core に material などの buffer 内容を定義する構造体を追加しない。

登録先は IArgumentTable とする。WebGPU は任意の GPU address や無制限の descriptor array を使えないため、登録された IGpuRef と CPU 側の要素単位 metadata から必要な binding を構築する。本 ADR は登録 API、要素参照、CPU 追跡と有限 binding への変換を一つの契約として定める。

buffer／texture／sampler の所有 API は [ADR-0005](0005-buffer-resource-contract.md)、[ADR-0006](0006-texture-resource-contract.md)、[ADR-0007](0007-sampler-resource-contract.md)、serializer と Slang は [ADR-0010](0010-shader-compilation-and-data-interop.md) に従う。wgpu の実装範囲は [ADR-0011](0011-wgpu-first-backend.md) に記載する。main に Graphics API は未導入であり、本 PR 内で設計と実装を更新する。

## 決定

### IArgumentTable の登録契約

Device が生成する IArgumentTable は backend が直接実装する所有オブジェクト。texture view、buffer range、sampler をそれぞれ独立した論理 slot に登録する。slot は利用者が管理する登録位置であり、shader binding 番号や GPU address ではない。texture と sampler を一対一の組にせず、一つの sampler を複数 texture が共有できる。

登録は `IGpuRef<T>` を返す。texture view は `IGpuRef<IGraphicsTextureView>`、sampler は `IGpuRef<Sampler>`、buffer は論理要素型 T の `IGpuRef<T>` とする。raw buffer の登録は ShaderRead または ShaderWrite 用途を要求し、pipeline／serializer が実際のアクセス互換性を検証する。初期 drawing serializer は read-only、compute は UInt32 の writable range に対応する。参照は backend の登録 identity・世代・型・range を内部で持ち、実アドレス、整数、CPU pointer、descriptor index への変換や任意 bytes からの復元を提供しない。利用者が interface を独自実装しても、その値を有効な登録として受理しない。

ArgumentTableDesc は登録可能数を resource 種類ごとに指定する。0 はその種類の領域なし、合計は正数。Label は診断専用。登録容量は一つの draw の容量を保証しない。共通 API に MaterialTextureProfile や物理 binding 方式を選ぶ profile を設けず、backend が表現と解決方法を決定する。論理 table と native heap／descriptor set／Metal Argument Table／WebGPU bind group は一対一に対応する必要がない。

### 利用側 API と Desc

比較元は origin/main。以下は .NET API diff 形式の追加 API であり、説明をコメントに記載する。公開契約の名前空間は Lumyte.Graphics。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed record ArgumentTableDesc
+    {
+        // 診断専用。native table の指定ではない。
+        public string? Label { get; init; }
+        // 独立した論理 slot 数。登録容量と同時 binding 上限は別。
+        public uint TextureCapacity { get; init; }
+        public uint SamplerCapacity { get; init; }
+        public uint BufferCapacity { get; init; }
+    }
+
+    public interface IArgumentTable : IDisposable
+    {
+        public uint TextureCapacity { get; }
+        public uint SamplerCapacity { get; }
+        public uint BufferCapacity { get; }
+        // 同じ Device の Sampled view を登録し、view と元 texture を保持。
+        public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view);
+        // texture と独立して登録。GPU data に使う参照の Count は 1。
+        public IGpuRef<Sampler> WriteSampler(uint slot, Sampler sampler);
+        // unmanaged shader buffer の範囲。Count は range.Count。
+        // 要素サイズは backend が解決し、転送・コピーは行わない。
+        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range) where T : unmanaged;
+        // serializer で pack した論理型。wire stride と metadata の一致を検証。
+        // BufferSlice<byte> の byte 数 / layout.ElementStrideInBytes が Count。
+        // raw buffer 登録と同じ buffer slot 領域を使う。内容を再 pack しない。
+        public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<byte> range, ShaderDataLayout<T> layout);
+        // metadata・引数・記録・GPU 使用中なら拒否。成功時に世代を失効。
+        public void ReleaseTexture(uint slot);
+        public void ReleaseSampler(uint slot);
+        public void ReleaseBuffer(uint slot);
+        // 全登録と派生要素が idle の場合に解放。失敗時は一部を解放しない。
+        public void Dispose();
+    }
+
+    public interface IGpuRef<T>
+    {
+        // 論理要素数。texture view／sampler は 1。アドレスや byte size ではない。
+        public ulong Count { get; }
+        // 登録範囲に相対的な index。戻り値の Count は 1。
+        // range、型、世代を保持する非所有参照。GPU work を行わない。
+        public IGpuRef<T> GetElement(ulong index);
+    }
+
+    public sealed class GraphicsDevice
+    {
+        // Desc を snapshot。backend の具象 instance を返す。
+        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc);
+    }
+
+    public sealed class GraphicsPipeline
+    {
+        // 単一要素ならその要素だけ、配列なら全候補の依存を収集。
+        // 引数が root 登録・metadata・必要 resource を保持する。
+        public ShaderArguments CreateArguments<T>(IGpuRef<T> data);
+    }
+
+    public interface IShaderDataWriter
+    {
+        // 利用者の serializer が型付き参照を渡し、backend が wire 表現を生成。
+        // null は参照なし。slot や binding index は利用者に返さない。
+        public void WriteTextureReference(string fieldName, IGpuRef<IGraphicsTextureView>? reference);
+        public void WriteSamplerReference(string fieldName, IGpuRef<Sampler>? reference);
+        // raw 読み取り buffer。要素参照なら論理 base と範囲を記録。
+        public void WriteBufferReference<T>(string fieldName, IGpuRef<T> reference) where T : unmanaged;
+    }
+}
```

WriteBuffer の shader data overload は serializer が生成した schema と論理型を検証する。初期 wgpu は転送完了を観測済みの read-only range を受理する。登録と GetElement は SizeInBytes／Count／要素 stride を丸めず、native binding alignment は別途検証して包含範囲と論理 offset で解決する。

GetElement の index は現在の参照の先頭に相対的。index >= Count は ArgumentOutOfRangeException。単一要素から GetElement(0) を呼ぶと同じ論理参照を返せる。texture view／sampler も index 0 のみ有効。登録した buffer 配列から何番目の構造体かを指定でき、別の整数 material index や byte address を重複指定しない。

### 利用例と root 引数

以下の MaterialData、serializer は利用側の型。upload と GPU コピー・送信・完了確認は利用者が明示する。rootTable は GPU buffer より後に生成し、引数・記録の使用終了後、buffer より先に解放する。

```csharp
using var resources = device.CreateArgumentTable(new ArgumentTableDesc {
    TextureCapacity = 20, SamplerCapacity = 1,
});
var image = resources.WriteTexture(0, textureView);
var sampling = resources.WriteSampler(0, sampler);
var materials = new MaterialData[] { new(baseColor, image, sampling) };
var layout = shader.GetDataLayout<MaterialData>();
// upload／gpu は利用者が正確な wire size で確保した buffer。
upload.Slice(0, layout.GetSizeInBytes(1)).CopyFrom<MaterialData>(
    materials, layout, serializer);
using (var transfer = device.CreateCommandEncoder())
{
    transfer.RecordCopyBuffer(upload.Slice(0, upload.Count), gpu.Slice(0, gpu.Count));
    using var commands = transfer.Finish();
    device.Submit(commands).Wait();
}
using var rootTable = device.CreateArgumentTable(new ArgumentTableDesc { BufferCapacity = 1 });
IGpuRef<MaterialData> materialArray = rootTable.WriteBuffer(0, gpu.Slice(0, gpu.Count), layout);
IGpuRef<MaterialData> material = materialArray.GetElement(0);
using var arguments = pipeline.CreateArguments(material);
// render.Draw(arguments, drawDesc) は material の依存から binding を自動設定。
```

glTF の primitive は通常一つの material に対応するため、その要素の IGpuRef を root に指定すればよい。GPU が material を動的に選ぶ draw は候補配列の IGpuRef を渡し、その範囲全体を追跡する。shader の実行結果を CPU が予測する設計にはしない。root 引数が native push constants になる保証はなく、backend が必要な入口を用意する。

### 登録世代と所有権

IGpuRef は非所有参照であり、table の寿命を延ばさない。table は登録先 resource を保持し、派生要素は親 slot と同じ世代に属する。raw buffer の要素参照は親 range の base に backend が解決した stride を加えた exact range を持つ。shader data の要素参照は schema と metadata の要素位置も持つ。親の配列全体が部分上書きされた場合でも、変更されていない要素の metadata が有効ならその要素は参照できる。

参照を取得しただけなら slot を解放・置換でき、その全要素参照を失効させる。CPU pack の dependency、ShaderArguments、記録、GPU 使用が参照する slot／派生要素の解放・置換は拒否する。Dispose は全 slot と派生要素の idle を先に確認する。失敗時に一部だけを解放しない。再登録で古い参照を復活させず、別 Device や別 table の同じ slot へ誤って解決しない。

### CPU 側で追跡する情報

backend は Device 内で複数の論理 Argument Table を統合した登録表を持つ。キーは table identity、resource 種別、slot と世代であり、異なる Argument Table の同じ slot 番号を区別する。論理 Argument Table を一つの巨大な WebGPU bind group として生成することはしない。

serializer は参照 field の pack と同時に、次の情報を CPU metadata へ記録する。型・field path・offset・stride は Slang reflection と schema で検証し、resource identity は serializer が受け取った不透明参照から取得する。任意の raw bytes から参照を推測しない。

- buffer identity／内容世代、schema・wire ABI、登録された byte 範囲と要素数。
- 要素ごとの texture、sampler、raw buffer descriptor と shader data 範囲の参照。
- field の存在有無、期待する resource 型、shader visibility と使用可能な読み取り操作。
- nested data の参照先範囲と、登録された descriptor 世代。

追跡の根拠は登録した IGpuRef の identity と要素範囲であり、GPU bytes の整数だけから登録を逆算しない。metadata の各要素は「何番目の構造体がどの登録を必要とするか」の対応表になる。係数・色・material 名など、buffer 内容の意味を Core が定義しない。raw numeric buffer の参照はその buffer binding を追加するが、数値を descriptor と解釈しない。参照を持つ shader data 範囲だけを再帰的にたどる。

### フラットな参照と再帰探索

直接 resource の依存一覧と、別の shader data 要素への参照を区別して記録する。子の参照がない場合は直接一覧を使い、探索用 stack、visited set、循環検査を確保しない。子参照がある場合だけ探索し、世代付きの結果をキャッシュする設計とする。フラットな経路へ再帰処理を必須にせず、実際の負荷は別々に計測する。初期 wgpu の writer は top-level の直接参照に対応し、nested schema の serializer／Slang load helper と再帰キャッシュは追加設計・実装の対象とする。

### pack、コピーと metadata の伝播

CPU pack は利用者の idle な Upload byte 範囲に wire data と metadata を登録する。pack 成功前に全 field・参照・必要 bytes を検証し、失敗時に新しい登録や lease を残さない。buffer の SizeInBytes や count は調整しない。

RecordCopyBuffer は登録済み全要素に整列する完全な範囲のコピーで metadata をコピー先へ対応付ける。配列全体だけでなく、登録済み要素の境界に一致する部分配列も伝播できる。GPU 命令の byte alignment と用途は別途検証する。要素を切るコピー・raw 上書き・GPU 書き込みは重なった要素の metadata を失効させる。範囲を再 pack するまで、その要素の参照解決を拒否する。

記録時の metadata はそのコピーより後の命令にだけ有効な予定の状態とし、対応する依存を利用者が明示する。同じ Encoder の後続 draw は予定の状態を使用できる。別 Submission では完了を観測するか、サポートする明示的 queue 依存を満たす。未送信破棄・送信失敗・DeviceLost は予定の metadata と lease を回収する。古い完了通知で、上書きにより失効した内容世代を復活させない。

### root 引数からの到達集合の構築

1. CreateArguments が IArgumentTable で登録した root の IGpuRef を受け取り、Device・登録世代・論理型・range を検証する。
2. 単一要素の参照ならその要素だけ、配列範囲の参照なら範囲内の全要素を CPU 対応表から読む。
3. 各要素の参照を収集し、nested shader data 範囲も探索する。探索済みの buffer／世代／範囲を記録し、循環や共有参照で無限探索・重複収集しない。探索サイズと深さの上限超過は診断して拒否する。
4. texture view、sampler、buffer range を別々に deduplicate する。texture は同じ native image でも view format／subresource が異なれば別。sampler は同じ確定設定を安全に共有できるものをまとめる。buffer は同じ allocation・アクセス・互換 layout の範囲だけを backend が安全に統合する。
5. compiled BindingPlan に従って物理配置を決め、参照変換表と bind group を生成・キャッシュする。draw／dispatch 記録時に pipeline、layout、内容世代と全参照を再検証する。

root の整数 material index だけから shader の選択を静的解析して推測しない。CPU が指定できる単一 material は単一要素 IGpuRef で表す。GPU が material index を選ぶ draw は候補配列の範囲を渡し、全候補を集合に含める。参照 field が runtime branch で未使用になる可能性があっても、初期契約ではその field の候補を保守的に含める。

buffer に格納した参照を GPU が新たに生成・変更する方式は初期契約の対象外。書き込み可能な shader data を参照解決に使用しない。GPU-driven／indirect draw を後で追加する場合も、CPU に登録した候補集合と有効範囲を別途定める。

### 安定した wire ID と draw 用変換表

wire data は backend 内部の安定した参照 ID を保持する。この ID は Device・登録世代と関連し、Argument Table の論理 slot や draw 用 binding 番号と同一にはしない。複数 draw で物理配置が変わっても material buffer を書き換えない。同じ sampler を異なる texture が共有する場合も、texture ID と sampler ID は独立する。

WebGPU は到達集合に含まれる ID だけを持つ、draw 固有の変換表を GPU に渡す。初期方式は resource 種別ごとに安定 ID を局所 binding 番号順に並べた有限配列とし、Slang helper が検索する。buffer entry は局所 storage binding、base offset、範囲長と必要な型情報を含む。Argument Table 全容量に比例する巨大な dense table を毎 draw 作らない。lookup のコストとキャッシュ効果は実装時に測定する。

単一要素の論理 offset が native storage binding の alignment を満たさない場合は、検証済みの包含範囲を束縛して helper の base offset で位置を表す。包含範囲・上限・アクセスを検証し、論理要素数や SizeInBytes を丸めない。

shader は load した安定 ID を変換表で解決し、compiled helper の switch から具体的な texture／sampler／buffer binding にアクセスする。buffer は range を検査し、schema と一致する load helper で扱う。WGSL resource object を integer cast で生成せず、WGSL が認める具体的な個別 binding の操作へ lowering する。

texture と sampler を独立した番号で sample するため、helper は compiled layout が許す組合せへ lowering する。sample type と sampler category が不適合な組合せは serializer／引数構築でも拒否する。switch の組合せによるコードサイズは容量 variant の設計と検証に含める。fragment では一様な制御フローで求めた gradient を渡す sampleGrad、または明示 LOD の sampleLevel を使い、非一様な branch 内の暗黙 derivative に依存しない。

変換表にない ID、世代不一致、範囲外アクセスは無効 descriptor を参照しない。CPU が検出できる違反は記録前に拒否し、shader helper の防御的な範囲外処理は zero／無効値を返す。これは不正な参照を有効な別の resource に差し替える保証ではない。

### Slang 成果物と binding 容量

compiled artifact は texture の型別容量、sampler のcategory別容量、uniform／storage buffer の容量、stage visibility、予約された root／変換表の binding、wire ID・lookup helper の ABI を記録する。texture と sampler の一対一ペア数を共通契約にしない。glTF の一つの material でも通常5枚の texture を使い得るため、独立した種類別の容量を指定する。

容量は artifact の生成時に固定する。描画時に暗黙のコンパイルや capacity の補正をしない。容量 variant と helper はオフラインで生成して DLL へ埋め込み、WGSL をコミットしない。明示的なオンライン compiler provider も同じ artifact／metadata を生成する。library ABI と cache key は ADR-0010 に従い、参照追跡 schema と lookup ABI の版も含める。

宣言済みで未使用の個別 binding は、同じ型の到達集合にある有効 resource を複製して埋める。該当型の到達 resource がない場合に必要な neutral fallback は、pipeline 作成時に backend が明示された初期化契約の下で用意し、Device／pipeline 内で保持する。ユーザーデータの Upload や material の意味を推測する処理とは分離する。実際に参照される ID がない限り fallback へ sample／load しない。fallback の確保・初期化コストと、失敗時の pipeline 生成エラーを報告する。描画時に fallback texture の確保や user upload を追加しない。

### 上限と失敗

sampled textures、samplers、uniform buffers、storage buffers の stage ごとの上限は独立して検証する。bind group 数、binding 数、buffer binding size、dynamic offset と alignment などの共通制約も検証する。root と変換表、環境 map など material 以外の引数も含め、到達した実 resource 数だけでなく compiled layout の宣言数に対して limits を照合する。

一つの draw の到達集合が artifact の容量を超える場合は、その種類の必要数・容量・Device 上限を診断して記録前に拒否する。自動 split、draw の追加、並べ替え、atlas 化はしない。Renderer は描画順と geometry／material の対応を知るため、必要ならそこで分割する。通常の glTF primitive は一つの material の参照だけを収集できるため、シーン全体の登録数で分割する必要はない。

API 差分の比較元は origin/main（Graphics API は未導入）。物理 binding を操作する公開 API は追加しない。以下は未実装の backend 内部契約の概念的な抜粋。

```diff
+namespace Lumyte.Graphics.Implementation
+{
+    internal interface IBindingLoweringBackendContract
+    {
+        // 複数論理 Argument Table の登録表を解決。Device／世代／型と有効期間を検証する。
+        ResolvedDescriptor Resolve(DescriptorIdentity identity);
+        // root と要素単位 metadata の到達集合を収集。単一要素と配列範囲を区別する。
+        // shader 実行の予測、GPU readback、GPU work は行わない。
+        DependencySnapshot Collect(RootArgumentSnapshot root, ShaderReferenceSchema schema);
+        // resource 種別ごとに集約し、limits／compiled capacity を検証。
+        // layout・descriptor世代・buffer範囲・使用する変換表の一致をキャッシュキーにする。
+        BindingSnapshot Build(BindingPlan plan, DependencySnapshot dependencies);
+        // 検証済み snapshot と内部変換表を現在の command 記録へ配置する。
+        // 別 Submission、user staging の確保、user data のコピー、待機はしない。
+        void RecordBindings(CommandRecordingContext commands, BindingSnapshot snapshot);
+    }
+}
```

Snapshot／identity／schema／context は backend の非公開概念型であり、共通 Core の buffer 内容として公開しない。Draw／Dispatch は記録前に snapshot を準備し、検証失敗なら命令や lease を残さない。native 記録失敗は ADR-0009 の Faulted 契約に従う。

### 内部データ、キャッシュと寿命

bind group、root 引数、変換表は backend のコマンド用内部データ。backend が現在の記録に必要な内部領域を配置し、同じ Submission で可視になるよう native 同期を扱う。この処理はユーザーバッファの CopyFrom／CopyTo を自動 Upload へ変更するものではない。別送信や隠れた待機で material／texture の未完了 Upload を代行しない。

CPU dependency snapshot は登録世代と対象 buffer metadata を保持する。引数の有効期間から記録まで descriptor の上書きを拒否し、CommandBuffer は実際の resource と変換表を lease する。未送信破棄で記録 lease を返し、送信時は Submission 完了まで保持する。キャッシュは構造と有効な resource identity を区別し、関連世代の失効で entry を除去する。完了前に transient table の領域を再利用しない。

bind group の再利用キーには pipeline layout、resource 型・view・sampler category、native identity・世代、buffer offset／sizeを含める。変換表の再利用には安定 ID と局所番号の対応も一致する必要がある。bind group の物理配置が同じでも ID が異なる場合、古い変換表は再利用しない。

### native backend と Browser

DirectX／Vulkan は native descriptor indexing を使用する。Vulkan の buffer device address が使える場合、buffer は実アドレスで直接参照できるが、texture／sampler は対応する descriptor 機構を使う。Metal は native argument table／argument buffer と resource ID を使う。同じ不透明参照と metadata の契約で Device／型／世代／範囲・寿命を検証するが、WebGPU 用の有限個別 binding と switch／変換表方式を必須にしない。native wire 表現は native descriptor と Slang の能力に対応させる。必須機能不足を有限 binding へ自動 fallback しない。

wgpu は既存 .NET binding で bind group／内部 buffer を実装し、独自 .Native は不要。Browser は同じ artifact・schema・lookup ABI を使用し、JS／Wasm 側が登録表と snapshot を管理する。ホストの native compiler を実行時に必須としない。

## 検討した代替案

利用者に物理 binding 集合や共通 material profile を作らせる方式は、backend の配置・容量を利用側へ露出するため採用しない。整数 material index だけから依存を推測する方式、draw ごとに user buffer の参照を書き換える方式、GPU readback で参照を集める方式も採用しない。

## 結果

利用者は IArgumentTable に resource を登録し、`IGpuRef<T>` を構造体や描画 root に渡せる。buffer 配列の要素参照から必要な texture／sampler／buffer を backend が収集する。WebGPU の容量超過は診断し、自動分割はしない。metadata と lookup の管理コストが必要になるため、フラットな直接依存と再帰的依存を分けて最適化する。

## 検証方針

共通 API だけで slot の世代・別 Device・型・用途、派生要素の Count／境界／相対 index、slot 解放と再利用での全要素失効を検証する。参照を保持する metadata・引数・記録・GPU 使用中の置換／解放／Dispose を拒否し、失敗が部分解放を残さないことを確認する。

20個の texture と20要素の一つの material 配列を登録し、GetElement で取得した参照を各 draw に渡して全画素を確認する。raw buffer の途中の要素、共有 sampler、複数 table の同じ slot、単一要素と候補配列の容量差、完全要素コピーの metadata 継承、部分上書きで変更要素だけが失効することも GPU テストで確認する。nested schema・循環・キャッシュ・Browser／native backend は対応実装が完成した段階で検証し、初期 wgpu の結果と混同しない。

登録・要素選択・pack・追跡で user upload／GPU コピー／Submit／待機を実行しない。WGSL は成果物 DLL に埋め込み、コミットしない。実行結果は PR に記録する。

## 参考資料

- [NoGraphicsAPI](https://github.com/sebbbi/NoGraphicsAPI)
- [バッファと明示的コピー](0005-buffer-resource-contract.md)
- [コマンドバッファと送信完了](0009-command-buffer.md)
- [Slang コンパイルと data layout](0010-shader-compilation-and-data-interop.md)
- [wgpu の対応範囲](0011-wgpu-first-backend.md)
- [WebGPU limits](https://www.w3.org/TR/webgpu/#limits)
