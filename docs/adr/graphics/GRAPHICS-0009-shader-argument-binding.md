# ADR-GRAPHICS-0009: Argument Tableのシェーダー引数への接続

- 状態: 提案
- 日付: 2026-10-09

## 背景

[Argument TableとGPU参照](GRAPHICS-0005-argument-tables-and-gpu-references.md)はresourceの登録と不透明なIGpuRefを提供する。[shader artifact](GRAPHICS-0006-shader-compilation-and-modules.md)はtarget別codeとreflectionを保持し、[PSO](GRAPHICS-0008-pipeline-programs-and-render-state.md)はprogramと描画状態を分離する。これらをdraw／dispatchへ接続し、利用者による物理binding番号の指定を不要にする。

Argument Tableのslotは種類別の論理登録位置であり、shaderのbinding番号ではない。tableを設定するだけで全登録を一度にshaderへ渡す方式では、登録容量が物理binding上限に制約される。不透明参照をshader引数へ渡し、必要な登録だけをbackendが解決する契約が必要となる。

## 決定

### Table選択と引数指定を分離する

機能名は「シェーダー引数バインディング」とする。IRenderEncoderとIComputeEncoderにSetArgumentTableとSetShaderArgumentsを追加する。tableは参照の解決元、shader argumentsはそのdraw／dispatchが使うroot引数であり、別々に設定する。

最初の契約では一つのtableを選択する。texture・sampler・bufferの登録領域はそのtable内で独立している。backendが実際に設定するheapやbinding集合の数は共通APIへ露出しない。未使用slotをbindingへ含めず、textureとsamplerのペアを強制しない。

shader argumentsはdeviceから生成されるbackend具象型とし、共通の所有wrapperやbackend contractを設けない。対象programのartifact reflectionからfield path・型・stage・resource用途を検証する。CPU側の設定値を保持し、draw／dispatch時にsnapshotする。設定APIだけでGPUコピーやcommandを発行しない。

### 公開API

比較元はmain。以下は提案するAPI差分で、公開契約はGraphics.Abstractionsに置く。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // programは同じdeviceの生存中の具象型に限る。
+        // graphicsの全stageを含む引数schemaをartifact reflectionから作る。
+        IShaderArguments CreateShaderArguments(IGraphicsPipeline program);
+
+        // compute entryの引数schemaをartifact reflectionから作る。
+        IShaderArguments CreateShaderArguments(IGraphicsComputePipeline program);
     }
+
+    // 利用者のmaterialなどbuffer内容を定義する型はapplicationに置く。
+    // このinterfaceはroot引数の設定を保持し、GPU resource自体は所有しない。
+    public interface IShaderArguments : IDisposable
+    {
+        // parameterPathはartifact reflectionに含まれる論理field path。
+        // native binding番号やtarget固有のlayoutを指定しない。
+        // 型・用途・rangeの不一致はArgumentException。
+        // 独自IGpuRef実装や他deviceの参照もArgumentException。
+        // 失効済みの登録はInvalidOperationException。
+        void SetBuffer<T>(string parameterPath, IGpuRef<T> value)
+            where T : unmanaged;
+
+        // texture viewの次元・sample typeをschemaへ照合する。
+        void SetTexture(string parameterPath, IGpuRef<IGraphicsTextureView> value);
+
+        // comparisonとfilteringの要件をschemaへ照合する。
+        void SetSampler(string parameterPath, IGpuRef<IGraphicsSampler> value);
+
+        // resource参照を含まないroot値。型・layoutはreflectionへ照合する。
+        // sizeof(T)のmemcpyだけでshader layoutが一致したと仮定しない。
+        // 未対応型・field layoutはNotSupportedException。
+        void SetValue<T>(string parameterPath, T value)
+            where T : unmanaged;
+    }
+
     public interface IRenderEncoder
     {
+        // 同じdeviceの生存中のtableを選択する。
+        // 次のdrawへ使うtableであり、全slotのbinding命令は発行しない。
+        void SetArgumentTable(IArgumentTable table);
+
+        // 現在のprogramに対応するroot引数を選択する。
+        // 値と登録identityのsnapshotはdraw時に作る。
+        void SetShaderArguments(IShaderArguments arguments);
     }
+
     public interface IComputeEncoder
     {
+        // renderと同じ選択・検証・寿命契約。
+        void SetArgumentTable(IArgumentTable table);
+
+        void SetShaderArguments(IShaderArguments arguments);
     }
 }
```

SetPipelineはtable選択や引数を暗黙に書き換えない。programを切り替えた場合、draw／dispatch時に引数schemaとの互換性を検証する。別programの引数は、reflectionの偶然の一致だけで受け入れずArgumentExceptionとする。

bindingのないshaderはtableとargumentsを設定しなくても実行できる。resourceまたはroot値を使うshaderでは必要な設定と全required fieldを要求し、不足をInvalidOperationExceptionとする。新しいpassでは設定状態を初期化し、別passから継承しない。

### 反射情報とtarget別ABI

parameterPathはtarget固有のbinding番号と分離する。同じlogical pathが複数stageで使われる場合、型と用途の互換性をprogram作成時に検証する。target別layout、binding plan、root値の配置、GPU参照のhelper ABI、schema versionはcompile時に確定してopaque artifact内へ保存する。runtimeで利用者がlayout補助情報を渡す方式にしない。

offlineは全targetを含む一つのartifactをDLLへ埋め込み、onlineは指定targetまたは全targetを含む同じformatを生成する。追加ABIに対応しないartifactは明示的にNotSupportedExceptionとする。生成WGSLやreflection JSONをソース管理へ追加しない。

buffer参照は登録rangeと要素位置を保持する。GetElementで得た参照のbyte位置をbackendが解決し、生addressや整数への変換は提供しない。有限bindingでは、元rangeのbindingと要素位置を別々に表現し、単一要素offsetをnative bindingのalignmentへ暗黙に丸めない。元range自体がalignment・用途・サイズ制約を満たさない場合は拒否する。参照に対応するshader helperのABIがない通常のresource parameterに、要素参照を無理に当てはめない。

### Draw／dispatchでの解決

1. program・table・arguments・各IGpuRefのdevice、登録identity、生存、用途、型を確認する。
2. root引数から参照した登録を収集し、selected table以外の登録を拒否する。
3. resource種類別に同じ登録と同じrangeを重複排除し、shaderのreflectionと有効なdevice limitsへ照合する。
4. backendがtarget別ABIへpackし、必要なresourceとroot値をcommandへ設定する。
5. native命令の記録に成功した時点のsnapshotと登録identityをcommandへ関連付ける。

texture・sampler・bufferの上限は種類別に検証する。一回のdraw／dispatchが上限を超えたらNotSupportedExceptionとし、自動的に描画を分割しない。tableのcapacityはこの上限と独立したままにする。

direct address／native descriptorで解決できるbackendへ有限bindingの変換表を強制しない。物理binding生成、cache key、native objectの所有と破棄の詳細は各backend READMEへ記載する。

### Snapshot・寿命・失敗時

draw／dispatch後のarguments変更は記録済みcommandを変更しない。tableのslotを後から置換・解放した場合、古いIGpuRefを新しい登録へ読み替えない。submit前に記録済みidentityの有効性を再検証し、失効したcommandのsubmitを拒否する。submit後の登録変更やresource解放に必要なGPU同期は利用者が管理する。

IShaderArgumentsはprogramを保持し、argumentsが生存しているprogramのDisposeを拒否する。引数内のIGpuRefは非所有で、登録を保持する権利を追加しない。argumentsのDisposeはそのCPU設定を解放するが、program・table・登録resourceをDisposeしない。記録済みcommandはarguments objectの生存ではなくsnapshotを使う。

不正なsetterは以前の値を維持する。draw／dispatchの事前検証失敗時はnative draw／dispatchを記録しない。GPU使用中のobjectを自動で待機・変更・破棄しない。CPU／GPU同期は利用者が管理し、内部lock、atomic ownership、InternalsVisibleToを追加しない。

### GPU buffer内の参照との境界

この引数契約はrootに直接渡されたIGpuRefを解決する。material配列などGPU bufferの中に含まれる参照は、raw bytesの整数から推測しない。applicationの論理structをpackするserializerと、要素ごとの依存metadataが必要となる。

そのserializerとcopy／上書き／GPU書き込み時のmetadata伝播・失効は別のAPI設計で扱う。後続でroot要素から依存を収集する際も、table選択、snapshot、identity、上限と寿命の本契約を使う。直接参照のフラットな経路に再帰探索を強制しない。

## 検討した代替案

- tableの論理slotをshader binding番号へ流用する: 種類別slotとtarget別の物理配置を混同するため採用しない。
- table全体を常にbindingする: 登録容量が物理上限へ制約され、未使用resourceも保持するため採用しない。
- shader sourceとは別のruntime binding layoutを利用者へ要求する: compile済み情報をartifactへ保持する方針に反するため採用しない。
- IGpuRefをaddressや整数へ変換してroot値へ入れる: 不透明参照と登録失効の契約を破るため採用しない。

## 結果と影響

利用者は論理tableと型付き参照を使い、物理binding配置をbackendへ任せられる。root引数をprogram reflectionに照合することでtarget間の違いを隠せる。一方、shader artifactへhelper ABIとbinding planを含めるcompiler側の接続が必要で、tableを設定するだけで任意のGPU bufferの内容を自動解析できるわけではない。

## 検証方針

shared projectの検証は共通APIのみを使う。texture・sampler・bufferの実アクセス、buffer単一要素、sampler共有、不要slot、同じ論理slot番号の種類別登録、引数のsnapshot、欠落・型不一致・別device・別table・失効参照を検証する。

computeはstorage buffer更新後に明示的なbarrier、copy、submit、wait、map、CopyToで結果を読む。graphicsは異なるtextureを使ったdrawの全画素を確認する。bindingのないPSOの検証も維持し、wgpu・Vulkan・Browser/Wasmを既存CIで実行する。backend固有の生成・cacheの詳細と測定は各READMEに記録する。
