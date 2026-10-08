# ADR-0011: .NET バインディングによる wgpu バックエンド

- 状態: 採用
- 日付: 2026-10-07
- 更新日: 2026-10-08

## 背景

最初の Graphics backend は wgpu とし、既存の .NET binding を直接使う。Lumyte の C++ wrapper や .Native プロジェクトは追加しない。[ADR-0004](0004-graphics-device.md) の生成境界、[ADR-0008](0008-resource-bindings.md) の論理 Argument Table と [ADR-0012](0012-bindless-binding-lowering.md) の自動 binding 構築を共通 API のまま実行する。

Graphics API は main に未導入のため、この PR 内の設計・実装を更新する。旧 API の履歴や互換 wrapper を公開契約として残さない。各 ADR の広い設計と、この backend が対応する具体的な範囲を区別する。

## 決定

### 構成と依存

Lumyte.Graphics.Core は共通型、resource・command・serializer と内部 driver の契約を保持する。Lumyte.Graphics.Wgpu は Ahjo.Wgpu の managed binding と第三者 native runtime を直接参照する。生成層の Lumyte.Graphics が backend を選び、Graphics.CreateDevice で共通 GraphicsDevice を返す。Core は Ahjo と backend に依存しない。テストとサンプルは生成層と共通 API だけを使う。

Linux／Windows の desktop adapter と software Vulkan を対象とし、Browser の JS／Wasm backend、独立 DirectX／Vulkan 実装は別途実装する。wgpu に Lumyte 独自 .Native は不要。

### 対応する resource と command

buffer は `IGraphicsBuffer<T>` を backend が直接実装し、Count と backend 解決の stride から SizeInBytes を計算する。CPU Upload／Readback の CopyFrom／CopyTo と、GPU の RecordCopyBuffer は分離する。byte の GPU copy offset／count は4の倍数、ushort は2、uint は1の倍数として解決し、論理 count を暗黙に補正しない。一般的な Slang data は byte storage へ serializer で pack する。

Texture は single mip／layer の D2 RGBA8Unorm／Srgb と sampled・明示 copy を扱う。描画先は RGBA8Unorm。backend の texture／view が共通 interface を直接実装する。sampler は通常の min／mag filter と U／V address に対応する。comparison、anisotropy、追加 dimension／format／mip は未対応として拒否する。

CommandEncoder は明示的な buffer／texture コピー、UInt32 compute、render pass と Finish を扱う。Submit は一度だけ実行し、Submission の完了まで記録した resource を保持する。CPU readback は利用者が staging 確保・コピー・送信・完了観測・CopyTo を行う。同期は wgpu の native resource scope に従い、独立した公開 Barrier 拡張は ADR-0009 の設計対象とする。

描画は triangle list、通常／index draw、viewport／scissor、clear／load／store、単一 RGBA8 target を扱う。depth／stencil、MSAA、複数 attachment、window／swapchain は別途実装する。

### 論理 Argument Table と shader data

texture、sampler、buffer を独立した logical slot に登録する。登録容量は draw の容量ではない。Argument Table が resource を lease し、registration 世代と Device 所属を検証する。CPU serializer は numeric field と不透明 descriptor 参照を pack し、要素ごとの CPU dependency metadata を残す。

対応する logical element は利用側が定義し、`IShaderDataSerializer<T>` を実装する。Slang wire struct も利用 shader が定義する。Core に material の係数や field 名・固定 stride は置かない。初期 schema は top-level float／int／uint と float Vector2／3／4、UInt32 の resource field に対応する。nested／array field と生成 serializer は拡張対象。

API 差分の比較元は origin/main（Graphics API は未導入）。以下はこの backend が共通 API として提供する宣言の抜粋。

```diff
+namespace Lumyte.Graphics
+{
+    public sealed class GraphicsDevice
+    {
+        // independent capacities。resource の Upload や送信をしない。
+        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc);
+        // 完了済みの metadata、型、用途と要素境界を検証。待機・送信はしない。
+        public GpuReference<T> CreateShaderDataReference<T>(BufferSlice<byte> range);
+    }
+    public sealed class ShaderModule
+    {
+        // DLL 埋め込みの Slang reflection から利用側型の wire stride を解決する。
+        public ShaderDataLayout<T> GetDataLayout<T>();
+    }
+    public sealed class ShaderDataLayout<T>
+    {
+        public ulong ElementStrideInBytes { get; }
+        // count > 0。checked。要素数と byte 数を暗黙に丸めない。
+        public ulong GetSizeInBytes(ulong count);
+    }
+    public static class ShaderDataTransfer
+    {
+        // exact-size の既存 Upload 範囲へ CPU pack と要素依存を登録するだけ。
+        public static void CopyFrom<T>(this BufferSlice<byte> destination, ReadOnlySpan<T> values, ShaderDataLayout<T> layout, IShaderDataSerializer<T> serializer);
+    }
+    public sealed class GraphicsPipeline
+    {
+        // root 範囲の要素依存から物理 binding と変換表を自動構築する。
+        public ShaderArguments CreateArguments<T>(GpuReference<T> data);
+    }
+}
```

### WebGPU の binding lowering

Slang library module は固定容量の8 texture、4 sampler、4 read-only buffer descriptor、root data storage buffer一つと内部 lookup uniform buffer一つを宣言する。これらの容量は独立する。compiled layout の全宣言を stage ごとの Device limits と bindings per group に照合する。必要集合が容量を超えた場合は記録前に拒否し、draw を分割したり shader を再コンパイルしたりしない。

root が一要素ならその依存だけ、配列範囲なら全候補を収集する。複数論理 Argument Tableの登録は Device 内の stable identity で区別し、同じ view／sampler／buffer 範囲の alias を同じ内部 ID に集約する。各登録の失効・lease は別々に検証する。GPU wire ID は draw の局所番号と分離し、material buffer を draw ごとに書き換えない。

内部 lookup uniform は root の要素 offset／count、texture ID、sampler ID、buffer ID・base offset・length を保持する。小さな有限表を Slang helper が検索し、個別 binding の switch から sampleGrad または UInt32 buffer load を行う。root の単一要素 offset が native storage alignment を満たさなくても、包含 allocation のbindingと内部 offsetで解決する。包含 byte 数は native上限へ検証し、bufferの論理サイズを変更しない。

backend の lookup は mapped-at-creation の内部 uniform として初期化し、引数・CommandBuffer・Submission の寿命で保持する。ユーザーデータの staging を確保・Upload したり、別 Submit／GPU 待機を追加したりしない。未使用 texture／sampler binding の neutral resource は pipeline 作成時に用意する。無効 ID の helper はこれらをアクセスせず zero を返す。

sample type は filterable float D2、通常 sampler に限定する。fragment gradient は caller が一様な制御フローで計算して渡し、WGSL uniformity 診断を無効化しない。sampleLevel、nested shader-data reference、generic root struct、binding cache と transient table の再利用は拡張対象。

### shader の成果物

Slang を基準とし、offline compiler が生成する WGSL と reflection JSON は DLL の embedded resources として読み出す。生成 WGSL／JSON はコミットしない。consumer は backend の library module を import するが、buffer内容を定義するstructはconsumerのmoduleに置く。runtime は Slang compiler を自動起動しない。明示オンライン compiler provider は ADR-0010 に従う別の実装対象。

### 所有権と検証

Argument Table 登録はview／sampler／bufferをleaseする。serializedmetadataは登録を保持し、buffer内容の失効・解放でleaseを返す。引数はroot buffer・metadata・pipeline・lookupを保持し、記録・GPU使用中にDisposeを拒否する。未送信破棄、部分上書き、失敗した送信とDeviceLostで予定のmetadataを失効させる。古い完了で失効を取り消さない。

失効参照、別Device、schema不一致、用途・範囲違反は引数例外、disposed登録はObjectDisposedException、lease中の変更・解放はInvalidOperationException、未対応schema／capacity超過はNotSupportedExceptionを返す。広い共通Result／GraphicsError契約はADR-0004で定め、未対応な範囲を成功扱いにしない。

## 検討した代替案

### wgpu 用 C++ wrapper と独自 Native package を作る

既存.NET bindingがdevice／resource／pipeline／commandを扱えるため、独自ABIの維持を増やさない。第三者runtimeの依存とLumyteのNative projectを区別する。

### optional binding array を必須にする

portable WebGPUの成立条件と一致しない。個別bindingとlibrary helperで実現し、Device limitsを明示的に検証する。

### test と sample で backend を直接使う

共通契約の不足を検出できないため採用しない。testとsampleは共通生成層と公開APIのみを参照する。

## 結果と影響

- 論理 Argument Table と不透明参照の共通APIを、既存.NET bindingで実行できる。
- 利用者は個別のbind group構造やtexture／samplerのペア容量を操作しない。
- 型別のcompiled capacityとDevice limitsによる一drawの制約は残る。
- CPU dependency metadataとGPU lookupに追加コストが生じ、cacheと再利用は実測して拡張する。
- 対応範囲を超えるschema・format・機能は拒否する。

## 検証方針

共通APIのGPUテストで、computeとreadback、triangle、indexと状態遷移、descriptorのDevice／世代／lease、serializerと反射stride、単一要素と候補配列の参照解決、容量、コピー・上書き・未送信破棄を確認する。20枚の独立textureを論理 Argument Tableへ登録し、一つのGPU配列の各要素をrootとして20drawするシーンの全20,480画素を検証する。

Linux software Vulkanで実行し、Windows／Browser／実GPUの性能を同じ検証結果として扱わない。実行結果はPRへ記録する。

## 参考資料

- [論理 Argument Table と bindless API](0008-resource-bindings.md)
- [参照追跡と binding lowering](0012-bindless-binding-lowering.md)
- [コマンドバッファ](0009-command-buffer.md)
- [Slang と shader 成果物](0010-shader-compilation-and-data-interop.md)
- [Ahjo.Wgpu](https://www.nuget.org/packages/Ahjo.Wgpu/)
