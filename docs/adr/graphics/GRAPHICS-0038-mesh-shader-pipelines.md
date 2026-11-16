# ADR-GRAPHICS-0038: 任意機能としての Mesh シェーダーパイプライン

- 状態: 提案
- 日付: 2026-11-16
- 基準コミット: `97accb2ca833a173b88855822b2cd63b8c973c96`
- 変更関係: 一部変更
- 変更元: [ADR-GRAPHICS-0008](GRAPHICS-0008-pipeline-programs-and-render-state.md)「分離する責務」、[ADR-GRAPHICS-0006](GRAPHICS-0006-shader-compilation-and-modules.md)「公開API」
- 変更対象: 描画プログラムのステージ構成、コンパイル対象の選択、描画命令と同期ステージの追加
- 継続する判断: プログラムと描画状態の分離、成果物と GPU モジュールの分離、利用者による寿命・同期管理

## 背景

低レベル描画 API の利用者は、GPU のワークグループで頂点とプリミティブを生成し、そのままラスタライズしたい。基準コードには Mesh 対応を表す機能フラグがあるが、Vertex 必須のパイプラインと通常の描画命令しかなく、この処理を表現できない。

基準コミットの `src/Graphics/Lumyte.Graphics.Abstractions/GraphicsPipelineDesc.cs` は `VertexShader` を必須とし、`ShaderStage.cs` は Vertex、Fragment、Compute のみを持つ。`IRenderEncoder.cs` には Mesh の起動命令がない。`src/Graphics/Lumyte.Graphics.Shaders/SlangShaderCompiler.cs` と `tools/shaders/Slang.targets` は既定で WGSL、SPIR-V、DXIL の三対象を生成するため、対象ごとに対応するステージが異なることも扱う必要がある。

## 決定

Mesh 専用のプログラム記述・所有インターフェイスと起動命令を追加し、既存の描画状態、引数、render pass と送信経路を共有する。Mesh と、その起動数を生成する Task ステージを任意機能として扱い、非対応環境に暗黙の代替実行を持ち込まない。

### 利用者の変更前・変更後

変更前には Mesh ステージを実行する方法がない。代替手段として、Compute で生成済みの頂点を Vertex シェーダーが読み、次の通常描画を記録できる。これはワークグループから直接プリミティブを出力する機能とは異なる。以下の `vertexPipeline` は実在する `CreateGraphicsPipeline` で作成したプログラムである。

```csharp
using Lumyte.Graphics.Abstractions;

IRenderEncoder render = commands.BeginRenderPass(pass);
render.SetPipeline(vertexPipeline);
render.SetRenderState(state);
render.SetViewport(viewport);
render.SetScissor(scissor);
render.SetBlendConstant(new(0, 0, 0, 0));
render.SetStencilReference(0);
render.SetArgumentTable(table);
render.SetArguments(in arguments);
render.Draw(vertexCount);
render.End();
```

変更後は次の提案 API で、Task なしの Mesh プログラムを作成し、Mesh ワークグループを直接起動できる。Task を指定すると、同じ起動命令の個数は Task ワークグループ数を表す。

```csharp
using Lumyte.Graphics.Abstractions;

if ((device.Caps.Features & GraphicsFeatures.MeshShader) == 0)
    throw new NotSupportedException("Mesh shaders are unavailable.");

IMeshPipeline pipeline = device.CreateMeshPipeline(new()
{
    MeshShader = meshShader,
    FragmentShader = fragmentShader,
});
IRenderEncoder render = commands.BeginRenderPass(pass);
render.SetPipeline(pipeline);
render.SetRenderState(state);
render.SetViewport(viewport);
render.SetScissor(scissor);
render.SetBlendConstant(new(0, 0, 0, 0));
render.SetStencilReference(0);
render.SetArgumentTable(table);
render.SetArguments(in arguments);
render.DrawMeshTasks(meshGroupCount);
render.End();
```

例は shader、table、引数、pass と状態の準備、転送と barrier、command の確定・送信・完了待機を省略している。例の `state` は TriangleList、strip index format なしで、Mesh の三角形出力に一致する。pipeline と shader を含む参照資源は GPU 使用完了まで保持し、その後に利用者が解放する。機能確認だけでは個々のプログラムの上限適合は保証せず、生成時に別途検証する。

### 対応機能と制限

`DeviceCaps.Features` の既存 `MeshShader` は、実際に有効化され、共通 API から生成・実行できる Mesh 機能を表す。新しい `TaskShader` は Task と Mesh の組を実行できることを表し、Task のみ対応という状態は返さない。ハードウェアが対応していても backend が実装・有効化していなければフラグを返さない。

Mesh と Task の制限は別々に報告する。軸ごとの個数・積、共有メモリ、Task の出力 payload と Mesh の入力 payload、出力頂点・プリミティブ数と出力メモリ、および payload との合計制約を区別する。Task から生成される Mesh グループ数にも Mesh の上限を適用する。backend は native の固定上限と取得可能な上限を正規化し、丸めや組合せを含む追加の native 制約も生成時に検証する。

非対応の制限オブジェクトは null とし、数値 0 を無制限の意味では使わない。Mesh が非対応でもデバイスは生成できる。利用者が従来描画などの別経路を明示的に選ぶ責任を持つ。

### プログラム、状態と命令

Mesh パイプラインは必須の Mesh、任意の Task と Fragment を同一デバイスから受け取る。ステージ、entry、ワークグループサイズ、最大出力数、プリミティブ種別、ステージ間入出力と payload の ABI は成果物の metadata と reflection から取得し、Desc に重複指定しない。Task と Mesh の payload、Mesh と Fragment の通常出力・プリミティブ単位出力の型、位置、補間属性を照合する。Fragment の省略は既存の attachment、depth/stencil と rasterization の契約に従う。

`SetPipeline` は選択中のプログラム種別を切り替える。引数は同じ `IArgumentTable` と `IShaderArguments` の契約で全ステージに渡し、ステージ間で共有する root schema と資源 ABI の整合を検証する。引数に関する既存の保証は [ADR-GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md) の「決定」を継続する。

共通の Mesh 出力は Line、Triangle の list に限定し、Point 出力は生成時に `NotSupportedException` とする。`GraphicsRenderStateDesc.Topology` はそれぞれ対応する list と一致させ、`StripIndexFormat` は null とする。strip、primitive restart、頂点バッファ入力と index buffer は Mesh 命令に適用しない。以前に設定した index buffer は保持できるが Mesh 命令から参照せず、通常の indexed draw へ戻ったときに既存の規則で使用する。

`DrawMeshTasks(x, y, z)` は Task がなければ Mesh、あれば Task のグループ数である。各軸は正数で、起動されるステージの各軸上限と積上限を、オーバーフローしない計算で検証する。出力が不要な workgroup はシェーダー内で出力数を 0 にできる。Task による Mesh 起動数、実行時出力数、同期とバッファ内容の正しさはシェーダー作者の責務であり、CPU は内容を走査しない。

`Draw`、`DrawIndexed` と既存の indirect draw は通常パイプラインを、`DrawMeshTasks` は Mesh パイプラインを要求する。選択なし、種別不一致、必要な状態・引数の欠落は `InvalidOperationException`、個数範囲外は `ArgumentOutOfRangeException` とし、native 命令の発行前に拒否する。Mesh 用の indirect 起動・count buffer はこの判断の対象外とする。

### コンパイル成果物

Vertex、Fragment、Compute の対象選択は維持する。Mesh と Task の `ShaderCompilationDesc.Target == null` は SPIR-V と DXIL の両方を要求し、WGSL の明示指定は `NotSupportedException` とする。片方の compiler が失敗したときに黙って単一対象へ縮小せず、従来どおり診断付きの失敗を返す。単一対象だけ必要なら利用者が Target を指定する。

offline の `SlangShader` 項目にも任意の `Targets` metadata を追加し、未指定時は online と同じステージ別既定値とする。値は `wgsl;spirv;dxil` の集合から選び、空集合、重複、未知の値は build error、Mesh/Task と WGSL の組合せも build error とする。pack 処理は要求対象の実数と各 code/reflection を格納し、従来の三対象固定を廃止する。

Slang の Task は amplification ステージへ明示的に対応付け、DXIL の適切な shader model と SPIR-V の Mesh 拡張要求を設定する。必要 feature、ワークグループと出力上限の宣言、payload・入出力 ABI を target 別 reflection に保存し、判定に必要な情報が欠けた Mesh/Task 成果物は拒否する。enum の文字列化だけでコンパイラのステージ名を決定しない。

既存の shader binary の version 1 の枠と既存ステージ値を保ち、新しい読み手は従来成果物を読めるようにする。追加の要求情報は reflection の拡張として格納する。古い読み手が新ステージ成果物を拒否することは許容する。成果物の code、reflection、要求機能が矛盾する場合は `ArgumentException`、デバイスの target・機能・上限が不足する場合は `NotSupportedException` とする。

### backend と同期・寿命

Vulkan は `VK_EXT_mesh_shader` と対応する feature/property を取得し、Mesh と任意の Task を有効化する。Mesh 用 pipeline では従来の vertex input と input assembly に依存せず、Mesh の出力種別を使い、`vkCmdDrawMeshTasksEXT` で記録する。NV 固有拡張による別の実行経路は含めない。

D3D12 は Mesh Shader Tier を確認し、AS/MS を含む pipeline state stream、対応 command list の `DispatchMesh` を使用する。Task は amplification shader に対応する。両 backend とも viewport、blend、depth/stencil、attachment format/sample count を従来の描画状態から解決し、プログラム種別、全ステージ、出力種別を native PSO の cache key に含める。

Browser と現在の Wgpu backend は Mesh/Task の機能を返さず、対応 pipeline の作成を `NotSupportedException` にする。shader module の作成でも対象欠落や非対応ステージを明確に拒否し、通常機能を使用するアプリケーションに Mesh compiler を要求しない。

`PipelineStage` に MeshShader と TaskShader を追加し、`AllGraphics` と `AllCommands` に含める。Copy または Compute の書込みからこれらの読取りへ、利用者が barrier を設定する。Vulkan は拡張の stage/access に変換し、D3D12 は利用可能な barrier model の non-pixel shader 読取り等へ保守的に対応付ける。flags の数値が新しい AllGraphics/AllCommands と完全一致するときは、そのデバイスで使用可能なステージ集合へ展開する。それ以外で非対応の Mesh/Task bit を含む指定は拒否する。同一の数値を持つ明示的な bit 合成も区別せず同じ規則を適用する。旧バイナリに埋め込まれた合成値は従来の bit 集合として扱い、暗黙に Mesh/Task を追加しない。

資源登録、参照検証、shader visibility、root layout と資源収集にも Task/Mesh を含める。常時の所有カウンターや Submit 時の再走査は追加せず、[ADR-GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md) の「決定」に従う。生成 API は shader を借用し、返された pipeline とその native variant を所有する。利用者は shader、pipeline、table、参照資源を GPU 使用完了まで保持する。暗黙の Submit、待機、shader compile は行わない。生成途中の native object は失敗時に回収し、cache に残さない。

### 公開 API 差分

以下は基準コミットの `src/Graphics/Lumyte.Graphics.Abstractions/` の同名型との差分であり、追加型は同 namespace に置く。省略部分は既存宣言を維持する。制限値の単位は個数または byte とし、すべて backend が解決した読取り専用の capability である。

```diff
 namespace Lumyte.Graphics.Abstractions;

 public enum ShaderStage
 {
     Vertex = 0, Fragment = 1, Compute = 2,
+    Mesh = 3, Task = 4,
 }
 public enum GraphicsFeatures
 {
     MeshShader = 8,
+    // Task 対応は Mesh 対応を伴う。
+    TaskShader = 16,
 }
 public enum PipelineStage
 {
+    MeshShader = 512,
+    TaskShader = 1024,
-    AllGraphics = IndexInput | VertexShader | FragmentShader | ColorOutput | DepthStencil,
+    AllGraphics = IndexInput | VertexShader | MeshShader | TaskShader | FragmentShader | ColorOutput | DepthStencil,
     AllCommands = Copy | DrawIndirect | AllGraphics | ComputeShader,
 }
 public sealed record DeviceCaps
 {
+    // 未対応なら null。対応 flag と同時に報告する。
+    public MeshShaderLimits? MeshShaderLimits { get; init; }
+    public TaskShaderLimits? TaskShaderLimits { get; init; }
 }
+public sealed record ShaderWorkgroupLimits
+{
+    public uint MaxSizeX { get; init; }
+    public uint MaxSizeY { get; init; }
+    public uint MaxSizeZ { get; init; }
+    public uint MaxInvocations { get; init; }
+    public uint MaxGroupCountX { get; init; }
+    public uint MaxGroupCountY { get; init; }
+    public uint MaxGroupCountZ { get; init; }
+    public ulong MaxTotalGroupCount { get; init; }
+}
+public sealed record MeshShaderLimits
+{
+    public required ShaderWorkgroupLimits Workgroup { get; init; }
+    public uint MaxOutputVertices { get; init; }
+    public uint MaxOutputPrimitives { get; init; }
+    public ulong MaxSharedMemoryBytes { get; init; }
+    public ulong MaxPayloadBytes { get; init; }
+    public ulong MaxPayloadAndSharedMemoryBytes { get; init; }
+    public ulong MaxOutputMemoryBytes { get; init; }
+    public ulong MaxPayloadAndOutputMemoryBytes { get; init; }
+}
+public sealed record TaskShaderLimits
+{
+    public required ShaderWorkgroupLimits Workgroup { get; init; }
+    public ulong MaxSharedMemoryBytes { get; init; }
+    public ulong MaxPayloadBytes { get; init; }
+    public ulong MaxPayloadAndSharedMemoryBytes { get; init; }
+}
+public sealed record MeshPipelineDesc
+{
+    public string? Label { get; init; }
+    // 必須。同じ device の Mesh module を借用する。
+    public required IGraphicsShader MeshShader { get; init; }
+    // null は Task を使わない。同じ device の module を借用する。
+    public IGraphicsShader? TaskShader { get; init; }
+    public IGraphicsShader? FragmentShader { get; init; }
+    public bool AlphaToCoverageEnable { get; init; }
+    public PipelineOptimizationMode Optimization { get; init; } = PipelineOptimizationMode.PreferReuse;
+}
+public interface IMeshPipeline : IDisposable
+{
+    // 不変な生成記述。寿命と同期は本文「backend と同期・寿命」に従う。
+    MeshPipelineDesc Desc { get; }
+}
 public interface IGraphicDevice
 {
+    // native 資源を所有する program を返す。非対応・上限不足は NotSupportedException、
+    // device/stage/ABI 不一致は ArgumentException、解放済み資源は ObjectDisposedException。
+    IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc);
 }
 public interface IRenderEncoder
 {
+    // 同じ device の生存する Mesh program を選択する。
+    void SetPipeline(IMeshPipeline pipeline);
+    // 各軸は正数。個数上限と program 種別は本文「プログラム、状態と命令」に従う。
+    void DrawMeshTasks(uint x, uint y = 1, uint z = 1);
 }
 public sealed record ShaderCompilationDesc
 {
-    /// <summary>Gets an optional output target; null compiles every supported target.</summary>
+    // null は既存ステージでは三対象、Mesh/Task では SPIR-V と DXIL。
+    // Mesh/Task の明示 WGSL 指定は NotSupportedException。
     public ShaderTarget? Target { get; init; }
 }
```

`GraphicsRenderStateDesc.Topology` と `StripIndexFormat` は署名を変えず、Mesh の場合は本文「プログラム、状態と命令」の list 一致・null 条件を宣言コメントにも追加する。`IRenderEncoder.Draw`、`DrawIndexed`、`DrawIndirect`、`DrawIndexedIndirect` のコメントには、Mesh program 選択時の `InvalidOperationException` を追加する。既存の `GraphicsPipelineDesc` と `IGraphicsPipeline` は変更しない。署名を維持するメンバーの契約差分の抜粋を次に示す。

```diff
 namespace Lumyte.Graphics.Abstractions;
 public sealed record GraphicsRenderStateDesc
 {
-    /// <summary>Gets the exact primitive assembly mode.</summary>
+    // 通常描画では従来の assembly mode。Mesh は出力に一致する LineList/TriangleList のみ。
     public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
-    /// <summary>Gets the indexed strip restart format.</summary>
+    // 通常描画では strip restart format。Mesh は null を要求する。
     public IndexFormat? StripIndexFormat { get; init; }
 }
 public interface IRenderEncoder
 {
-    /// <summary>Records a direct draw with the explicit pipeline, state and arguments.</summary>
+    // 従来の direct draw。Mesh program 選択時は InvalidOperationException。
     void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
-    /// <summary>Records an indexed draw using the selected index range.</summary>
+    // 従来の indexed draw。Mesh program 選択時は InvalidOperationException。
     void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0);
-    /// <summary>Records one GPU-sourced draw without reading command data on the CPU.</summary>
+    // 従来の indirect draw。Mesh program 選択時は InvalidOperationException。
     void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments);
-    /// <summary>Records one GPU-sourced indexed draw with the selected index range.</summary>
+    // 従来の indexed indirect draw。Mesh program 選択時は InvalidOperationException。
     void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments);
 }
```

## 検討した代替案

- 既存 Desc の VertexShader を nullable にし、Mesh/Task を並べる: pipeline interface を増やさずに済むが、Vertex と Mesh の両方または両方なし、Task だけという無効な組合せを許す。必須条件を型で表現する専用 Desc を選ぶ。
- Compute と indirect draw に統一する: 対応範囲は広いが、生成結果の中間バッファと同期が必要で、Mesh からの直接出力を表現しない。利用者が選べる別手段として残す。
- すべての backend にエミュレーションを要求する: API の呼出し場所は統一できるが、同期・メモリ・対応可能な shader の制約を隠す。機能を明示し、非対応を返す。
- meshlet を共通 API の必須データ形式にする: 定型メッシュの利用は簡単になるが、手続き的生成を制約する。頂点の小分け形式や生成アルゴリズムは利用者の buffer と shader で表現する。

## 結果と影響

利用者は既存の描画 API を維持したまま、対応デバイスで Task/Mesh の直接実行を選べる。2D レンダラーへの接続、meshlet 作成、形状の分割、GPU 全体の分類、Mesh indirect 起動と描画順の自動調整は対象外であり、性能向上も保証しない。

backend 実装者は追加 interface メンバーと新しい stage に対応する必要がある。既存アプリケーションの呼出しは維持するが、外部の device/encoder 実装にはソース互換性の変更がある。flags の合成値を含む同期変換、成果物の読取り、offline build と online compiler の対象選択を一体として変更する。

受入れ条件は、Vulkan と D3D12 それぞれで Task なしの三角形描画、Task からの Mesh 起動、引数と資源読取り、Copy/Compute 書込みからの同期、通常描画との切替え、depth/stencil と blend の適用を確認できることである。Line 出力と Fragment 省略も対応状態で確認し、Point 出力を拒否する。別 device、誤った stage、payload/linkage 不一致、出力・共有メモリ上限超過、起動数の軸・積超過、解放済み資源、未対応 target は所定の段階で拒否する。

旧成果物の読取り、Mesh/Task の二対象と単一対象生成、WGSL 拒否、offline/online metadata の一致を検証する。Browser/Wgpu と非対応デバイスでは既存描画が継続し、Mesh/Task が明示的に拒否されることを確認する。対応 GPU がない環境の skip は native 実行の確認に数えず、文書検査と shader compile だけで GPU 対応済みとは判断しない。
