# Shader transfer migration validation

The breaking contract is documented in
[GRAPHICS-0035](adr/graphics/GRAPHICS-0035-csharp-authoritative-shader-transfer.md) and the
[generator README](../src/Graphics/Lumyte.Graphics.Generators/README.md).

## struct限定のshader引数とuser選択の要素範囲（2026-11-14）

共通storage契約を削除したcheckpointはd7eb189592f5e170b0399525e6eee0ed9beac7e4。
続く明示要求により、IShaderArgumentsを使うroot／typed buffer／slice／factory／Graph／helperのgeneric境界を
struct, IShaderArgumentsへ限定した。unmanaged制約へは置換せず、typed IGpuRefを持つmanaged outer structも許可する。
生のunmanaged型をinterfaceなしで転送する経路は変更しない。

生成対象はpartial struct／record structである。参照を含むouterはprivate unmanaged wire storageとtyped reference memberを持ち、
生成readonly Writeはwireだけをlocalへ固定してからraw区間と16-byte参照を物理順に書く。
managed outer全体をMemoryMarshalへ渡さず、数値field別の変換も行わない。
class／record class／ref structにはLUMG001を出し、generatorなしの手書きclassもroot／buffer／Graph呼び出しでCS0453となる。
partial numeric propertyのreadonly modifier／accessor accessibilityは定義と一致させ、get-only／readonly storage／
readonly outer／private setter／managed outerの実compiled fixtureを実行した。

class用GetTransferFields、DiagnosticSourceとnative null-check boxingを除去した。
CaptureはTypeとstatic generic metadata callbackだけを保持し、入力structをboxing／保持しない。
Capture／Copy中に診断getterは呼ばない。明示的な診断時だけgeneric static cacheからT.TransferFieldsを取得するため、
生成metadataを使う経路はDynamicMethod／MakeGenericMethodを必要としない。
手書きunmanagedのstatic metadata未指定時はJIT reflected-field fallbackを維持する。
任意Typeだけからの反射診断やstatic metadataのないraw型の動的fallbackはBrowser／NativeAOTでは利用できない。
Browser buildは検証したが、NativeAOT publish／実行は検証していない。

91のgeneric制約とproduction／samplesの17 class providerを移行し、rendererのinstruction再代入を確認した。
structは値copyであり、in parameterのconstrained interface callや非readonly custom getterにはdefensive outer copyが生じ得る。
zero-copy／転送全体のzero-allocationは主張しない。metadata／dependency ownerとbackend-private emulationのallocationは残る。
DrawingInstructionのnew()は従来のindex -1を維持するが、defaultとarrayの初期要素はconstructorを実行せず0／nullとなる。
参照は使用前に設定し、未設定IGpuRefはwriterで拒否する。

変更検出は追加しない。ユーザーがref values[index]へ変更し、既存CopyFrom(values.AsSpan(index, count), elementOffset)で
入力要素範囲とupload先を選び、既存CopyBufferのsource／destination sliceでGPU転送範囲を選ぶ。
auto dirty tracking、範囲coalescing、partial-field APIや新しいcacheはない。upload sourceのGPU消費までの寿命は従来どおりである。
実Vulkan／Wgpuのvalidation off/onで三要素の中央だけの数値／参照更新とcopyを行い、前後要素のreadbackとmetadata identityが
変わらないことを確認した。value-returning arrayアクセスを編集する場合はrefまたは明示的な書き戻しを使う。

最終source gate:

- Graphics Debug／Release各269成功・失敗0・95skip（364件）。
- RenderGraph Debug／Release各133成功・失敗0・skip0。
- 全ホストRelease3,242成功・失敗0・116skip。Drawing／Drawing2D／Effects／UI／samplesを現行generatorでcompile。
- targeted Debug／Release各47成功・失敗0・skip0。CPUのcontract／partial-property検査も含み、全件をGPU実行とは呼ばない。
- D3D12管理側Release50成功・Windows依存7skip。Browser Release compile／JS syntax成功。
- 生成class／ref struct negativeはLUMG001、generatorなしclass negativeはroot／buffer／Graph各CS0453。
- 遅延診断counterを実際に増加するgetterで検査し、AOT-safe static providerとJIT fallbackを追加検証した。
  Vulkan境界fixtureの早期失敗をcleanup例外が隠す問題はfinally Endで修正し、wire専用static metadataを与えて再実行した。
- 独立read-only reviewはAOT診断欠落を指摘し、修正後reviewはcritical blockerなし。

最終ログはartifacts/validation16-final-{fullhost,debug,hardware-debug,hardware-release,graph-debug,d3d-release,browser-release,markdown}.log。
negative compilerログはartifacts/validation16-negative-{consumers,generator,ref}.log、partial-property matrixはvalidation16-partial-*。
Windows native／Browser実行、Khronos validation layer実行、NativeAOT publish／presentationは未検証。
過去のclass benchmarkは元revision専用の凍結sourceとして保持し、最新API向けに書き換えたり旧値を流用したりしない。
新規性能campaign、CI／workflow変更、過去ADR変更、binary／gzip復元はない。

以下はstruct限定前の履歴であり、class providerの許可は最新契約ではない。

## 共通root storage契約の削除（2026-11-14）

IShaderRootStorageを削除し、alias／代替public storage interface／opaque object bagは残さない。
ShaderRootArgumentsはRootParameter、ShaderTypeName、WireSize、Referencesとdetached admissionだけのimmutable metadataとなる。
Storage／nullable storage field／Allocate factory／Seal／Dispose／IDisposableとpayload lifecycle stateを削除した。
Createはroot名・型名・論理size・参照metadata・optional prepareの五引数である。
共通ShaderBindingDataのRootSizeと共通seal呼び出しも削除し、数値やnative handleを共通rootへ格納しない。

Wgpu／Browserは既存のprivate concrete RootStorage自体がuniform bufferと一度だけ初期化したArguments metadataを所有する。
追加wrapper／array／dictionaryを作らず、backend writerとbindingはそのconcrete ownerを直接使う。
backend encoderがownerをcommand bindingsへ保持し、最初のbindでbackend-private seal／unmap、GPU完了とcommand解放後にdisposeする。
失敗した新世代は一度破棄し、cleanup例外で元の転送／prepare例外を隠さない。旧選択・複数Set／Drawの世代は維持する。

Vulkan／D3D12のSet即時native記録は変更しない。root metadataをIDisposable ownerとしてcommand listへ積むことは廃止した。
代わりに実Draw／Dispatchで作る既存native bindingがShaderBindingDependenciesをreadonly保持し、disposeまでRoot metadataと
全transitive resource／table identityを生存させる。GC.KeepAliveはこの既存bindingの解放境界を明示する。
未使用のSet metadataは再選択後に保持不要であり、数値payload保持や共通storageを復活させない。

共通storage API不在guardは旧36daf855で二件とも失敗し、現行で成功した。
actual Wgpu final-root testはvalidation off/onで実concrete ownerを観測し、command disposal後の解放を確認した。
独立read-only reviewはblockerなし。tests-only sink／fake backend ownerは共通production契約ではない。
型をstructへ限定する次の要求はこのstorage削除checkpointとは別の変更として扱う。
新規性能測定／zero-allocationの主張はなく、旧凍結結果は変更しない。

ログはartifacts/validation15-{fullhost,hardware-release,negative-control,positive-control}.logとmetadata-* evidence。
以下は共通storage削除前の履歴であり、共通Storage／Seal／Disposeの記述は最新APIではない。

## SetArguments内でのnative定数command記録（2026-11-14）

2aa25f99までのDraw時payload再送を廃止し、Vulkan／D3D12はSetArgumentsが戻る前にnative commandへ定数を記録する。
PSO objectを書き換えるのではなく、選択されたpipeline契約に対するcommand state更新である。

1. pass／pipeline／容量を検査し、DWORD-roundedの小さいstack spanを確保、tailをゼロにする。
2. backend ref-struct writerでraw C# wireとtyped referenceを同期符号化し、Complete、診断、prepareを完了する。
3. callback後にcommand ownerと捕捉済みpipelineの生存／identityを再検査し、参照metadataの所有を確保する。
4. Vulkan CmdPushConstants、D3D12 SetGraphicsRoot32BitConstants／SetComputeRoot32BitConstantsを呼び、その後に選択metadataをpublishする。

VulkanRootStorage／Direct3D12RootStorageとDraw用payloadを削除した。
数値byte array／ArrayPool／native vectorをDrawまで保持せず、driver command bufferがnative copyを持つ。
Draw／Dispatch側は参照metadata収集・admissionとdescriptor bindingを行うだけで、数値を再送しない。
残るroot objectは型名／論理size／参照metadata／detached admissionだけで、opaque storageはnullである。
共通ownerはnull storageも扱うgenericなmetadata ownerであり、backend分岐やuniform知識を持たない。
Wgpu／Browserはbackend-private uniform emulationと世代所有を変更していない。

公開IShaderValueWriterの二つの値一引数methodは変更しない。
stack-only writerを渡すためIShaderArguments.Writeのgeneric constraintへallows ref structを追加した。
生成実装と手書きconsumerを移行し、C#13以降／pinned .NET10で検証した。SpanはSet呼び出しを越えて保持しない。

固定契約とnative state:

- Vulkanはoffset0／device capacityが固定。compute layoutはCompute、graphics layoutはVertex|Fragmentという
  bind-point別の固定stage rangeを使い、更新maskをそのrangeと一致させる。
  VUID-vkCmdPushConstants-offset-01796に従い、全stage単一rangeへsubset maskで更新する旧案は採用しない。
  descriptor数で全PipelineLayout instanceは変わり得るが、同一bind-pointの全variantはpush-compatibleである。
  Compute Setがgraphicsの値を更新しない。物理上限は実機256 B、病的なimplementation向けstack guardは64 KiB。
- D3D12はDeviceで一度作るcached root signatureをcompute／graphics／partial／generic PSOが共有する。
  58 app DWORD＋table1＋table1＋draw2＋SRV2＝64を維持する。
  Commandsはgraphics／computeそれぞれのroot identityを追跡し、Draw時に同一signatureを再bindして定数を無効化しない。
  Setは実PSO variantの確定や新しいroot signature作成を待たない。

codec／prepare中の再入Set、pipeline変更、End、実行・render/index変更をnative encoderで拒否する。
Graph prepareに必要なSetArgumentTable autobindだけは許可する。
callbackが直接command owner／pipelineをDisposeした場合も、直後の生存検査でnative発行前に停止する。
host検査／codec／prepare失敗は以前の選択とnative定数を書き換えない。
ただしcaller自身によるDisposeや、既に発行したvoid driver commandのdevice failureはrollbackできない。

回帰と最終gate:

- immediacyのmanaged IL/order guardは旧2aaでVulkan graphics／compute、D3D graphics／computeの四case全て失敗し、現行で成功。
  旧failureはartifacts/validation14-negative-control-{vulkan,d3d}.logに保存した。
- 実Vulkan／Wgpuでcaller変更後もSet値を使うこと、二Set／二Drawと再Drawのred／green readback、
  pipeline切り替え、容量境界、prepare失敗後の以前の値、Graph／transitive参照を検査した。
- 独立reviewがstage mask不整合とcallback後UAF経路を検出し、修正後のreviewはblockerなし。
  修正前のgate値を最終証拠として流用せず、全gateを最終sourceで再実行した。
- Graphics Debug／Release各271成功・失敗0・95skip（366件）、Graph各133成功・失敗0・skip0。
- 全ホストRelease3,244成功・失敗0・116skip。targeted Debug／Release各42成功・失敗0・skip0。
- D3D12管理／source-order検査50成功・Windows依存7skip。Browser Release compile／JS syntax成功。
- Khronos validation layerがhostにないため、native VUID layer実行は未検証。GraphicsValidationと同一視しない。
  Windows native／Browser実行・AOT／presentationも未検証。D3D generic SetProgramはsource-order reviewのみ。

最終ログはartifacts/validation14-final-{fullhost,debug,hardware-debug,hardware-release,graph-debug,d3d-release,browser-release}.log。
この変更の性能再測定はしておらず、f27→3582の凍結結果を最新Setへ読み替えない。
CI／workflow変更、過去ADR変更、binary／gzip復元はない。

以下は2aa時点までの履歴であり、Draw時payload送信の記述は最新Set契約ではない。

## native定数rootとbackend-private WebGPU emulation（2026-11-14）

直前のbackend-native writer／uniform root checkpointは3582af8ada8c00ff38553eebc216bc64527171c1。
その時点のf27→3582 ABBA測定は凍結したままであり、以下のnative定数rootへ適用しない。

- Vulkanは実vkCmdPushConstantsを使い、application rootのdescriptor binding8とroot uniform bufferを削除した。
  physical deviceからMaxPushConstantsSizeを取得する。今回のRadeon8060S／RADVは256 Bであり、普遍的な値ではない。
  pipeline variantは同じoffset0／device max範囲、Vertex／Fragment／Compute stage flagsを使う。
  layout解決と依存admissionの後、Draw／Dispatch記録時にceil(wire bytes/4) DWORDをpushする。
- D3D12はapp parameter0、b8／space0を実SetGraphicsRoot32BitConstants／SetComputeRoot32BitConstantsへ置換した。
  64 DWORDからresource table1、sampler table1、draw-info parameter3の2、vertex SRV parameter4の2を引き、
  appは58 DWORD／232 B。固定合計64をnative static assertionで検査し、per-Set GPU CBV allocationと旧root exportsを削除した。
- どちらもuniform fallbackを持たず、容量超過はallocation／選択publish前に例外となる。
  大き過ぎるshader artifactもSet前にnative pipelineを作らないよう、全app root名のlogical extentを事前検査する。
  Vulkan260 B artifactは256 B adapterでCreateShader時に拒否、256 Bはshader／pipelineとも成功。
  D3D232 Bはphysical helper宣言240 Bでもlogical58 DWORDとして許可し、236 Bはnative PSO前に拒否する。
- Set時に確定した小さいbackend-owned CPU payloadを保持し、現在のpipeline layoutで記録するnative commandへ渡す。
  これはShaderValueSnapshotやCPU inspection objectではない。複数Set／Draw、再Draw、失敗後の旧選択を保持する。
  private payload、root owner、metadata／collector等のallocationは残り、zero-allocationとは主張しない。

共通runtimeからuniform知識を除去した箇所:

- ShaderRootArguments.Allocateは論理wire sizeをそのままfactoryへ渡し、共通のminimum16／alignment16を廃止。
- Create／Allocateはopaque payload capacityが論理size以上であることだけを検査する。
- IShaderRootStorageのWrite／Sealはgenericなbackend payloadの書き込み／freezeであり、uniform allocationやGPU unmapを要求しない。
- ShaderBindingDataのuniform capability前提を除き、backendへ移した。root capacity情報はopaque storageの実サイズ。
- 共通runtimeにbackend名／push／uniform feature分岐を追加していない。shader target emitterがstorageを選ぶのはtarget codegenの責務。

Wgpu／Browserは変更前と同じuniform emulationをbackend内に維持する。
private RootStorageだけがminimum16／alignment16、buffer確保／map／sealとuniform limitを知る。
per-Set世代をnative command完了まで保持し、Graph metadata／remapも従来どおり扱う。
ImmediateData、package／Slang version update、共通uniform fallbackは追加しない。

参照だけを持つ旧ShaderBindingSnapshotはShaderBindingDependenciesへ改名し、public Collect／private VisitReferencesとした。
旧型のaliasは残さない。取得するのはtable affinity／最新command-local transitive metadata／Graph宣言検査用の依存であり、
数値snapshotではない。ShaderValueSnapshot等の本当のsnapshotは改名・削除していない。

最終sourceの検証:

- Graphics Debug／Release各264成功・失敗0・94skip（358件）。
- RenderGraph Debug／Release各133成功・失敗0・skip0。
- 全ホストRelease3,237成功・失敗0・115skip。host consumer／samples buildを含む。
- targeted subset Debug／Release各36成功・失敗0・skip0（GPU-gated20、CPU16）。
  実Vulkan4／20／256 B dispatchと260 B Set拒否、20／256 B旧世代readback、oversized artifact早期拒否、
  Vulkan／Wgpu validation off/onのred／green二draw、世代・transitive依存・byte ABIを含む。
  4-byte caseはdispatch確認であり、直接readbackの検査ではない。
- D3D12管理側Release47成功・Windows依存7skip。Windows native実行／cross-buildはtoolchain不在で未検証。
- Browser Release compile／JS syntax成功。Browser実行／AOT／presentationは未検証。
- 独立read-only reviewで容量preflight不足と旧alignment testを検出し修正後、final reviewはblockerなし。

ログはartifacts/validation13-{debug,graph-debug,fullhost,hardware-debug,hardware-release,d3d-release,browser-release,markdown}.log。
shader disassembly／layout probesはartifacts/native-constant-shader-probesに保持する。
CI／workflow／通常build runner設定、過去ADR、旧raw測定・削除済みbinaryは変更／復元しない。
offline shader packのtarget storage metadataにはtools/shaders/Slang.targetsを機能変更として更新した。

以下はuniform-root checkpointまでの履歴であり、最新root transportと混同しない。

## backend自身によるwriter実装（2026-11-14）

Vulkan／Direct3D12／Wgpu／Browserがそれぞれ内部ShaderValueWriterでIShaderValueWriterを直接実装する。
位置・容量・完了・16-byte参照符号化・依存収集はbackend内で行い、Shared writerへの委譲ではない。
SetArgumentsの生成型／手書き型とshader-data CopyFromは同じbackend writerを使う。
参照はクリアしたstackalloc 16-byteへ直接符号化し、最終uniform／mapped uploadへ同期書き込みする。
GPU転送経路のShaderValue生成、byte[16]中間配列とFunc参照encoder呼び出しは削除した。
Vulkanは内部の型付きnative buffer handleを使い、handleのobject boxingも避ける。

SharedのShaderTransferWriter、IShaderTransferDestination、ShaderRootDestination、ShaderUploadDestination、
ShaderDataUpload、ShaderReferenceEncodingとVulkan／D3D12の旧配列encoderを削除した。
Shared root helperはAllocateによるaligned storage検査と、完成storage／metadataを受けるCreateによる
成功時だけの所有権取得に限定した。書き込み失敗時はbackendがstorageを破棄し、cleanup例外で元の例外を隠さない。
診断はType／opaque reference／位置を受けるoverloadで検査し、ShaderValue recordを構築しない。
明示CaptureのShaderSnapshotWriterとlogical ShaderValueはCPU検査用としてのみ残す。

公開writerは二つの値一引数methodのまま。C# wire／padding、参照slotの物理順と一回のgetter評価、
数値bufferの直接copy、Graphのresource／範囲／access使用時検査とcached参照許可は変更しない。
backend-private依存metadata、世代のGPU完了までの保持、Set失敗時の旧選択、CopyFrom失敗時の
要求範囲metadata全無効化と元例外を維持した。Browser bridgeのJS／GPUコピーは残り、zero-copyとは呼ばない。

fake-storageのunit testはShared所有権／診断の検査用であり、backend writer実行の代替とはしない。
実Vulkan／Wgpuでは88-byte全wire（数値／padding／sampler／texture／writable／二つのread-only参照）を
literal ABI oracleで照合し、reserved zeroを確認した。実backendのshort／overflow CopyFromは
要求3要素のmetadataを全て無効化し、外側2要素のidentityを維持する。実Setのmissing／short／overflow、
途中sentinel例外、prepare拒否後も以前の世代のreadbackを検査した。全てvalidation off/onで実行する。

残るallocationは依存Listとそのbacking、metadata object／参照array／read-only wrapper、rootとnative buffer所有者、
既存factory delegate、実行ごとのbinding dependency収集などである。転送全体のzero-allocationは主張しない。

最終sourceの検証:

- Graphics Debug／Release各243成功・失敗0・90skip（333件）。
- RenderGraph Debug／Release各133成功・失敗0・skip0。
- 全ホストRelease3,216成功・失敗0・111skip。host samples／consumer buildを含む。
- Vulkan／Wgpu targeted subset Debug／Release各20成功・失敗0・skip0（backend別GPU test16とCPU test4）。実backendのABI、
  malformed codec、世代保持、同一class root／buffer／Graphと更新されたtransitive依存を含む。
- D3D12管理側Release29成功・Windows native7skip、Browser Release compile／JS syntax成功。
- Windows native／Browser実行・AOT／presentationは未検証。独立source reviewでblockerなし。

ログはartifacts/validation12-{debug,graph-debug,fullhost,hardware-debug,hardware-release,d3d-release,browser-release,markdown}.log。

凍結public-client BDNのABBA比較は[性能記録](performance/backend-native-writers.md)に全process値を記載した。
Vulkan CopyFrom 64要素のmanaged allocationは24,640→11,264 B、paired medianは24.086%／27.047%低下。
Set／full-completionのlatency改善は順序で逆転または0.5%未満で、安定したend-to-end高速化とは主張しない。
raw結果はlocal artifactsに保持し、repositoryには小さいtext summaryだけを追加した。

以下はf27以前のflat writer／Graph連携の履歴であり、旧Shared factoryは最新APIではない。

## f27時点の値だけを渡すwriter／Graph連携履歴（2026-11-14）

最新の明示要求により、旧cursor-only契約を次の単一writerへ置き換えた。

- `WriteValue(ReadOnlySpan<byte> value)`
- `WriteReference<T>(IGpuRef<T>? value)`

両methodは値一つだけを受け、offset／size／pathを渡さない。writerが内部の書き込み位置を進め、
参照は常に16-byte、その場でbackend固有に符号化する。容量は減算で検査し、完了時に
TransferSizeとの一致を検査する。生成codecはprivate unmanaged wireをローカルに固定した後、
C#上の参照位置順にpadding込みraw区間・型付き参照・raw tailを直接書く。
numeric field別の変換、source Spanの保持、参照second passは行わない。

ShaderWire、ShaderWireCursor、IShaderReferenceCursor、IShaderReferenceEmitter、IShaderWireSinkと
WriteWireを削除した。生成側には型固有のprivateなoffset順planと診断情報だけを残す。
明示CPU検査のSnapshotWriterも同じ二methodを使い、参照slotはゼロにする。
raw buffer memcpy／GPU copyと内部destinationへのbyte書き込みは下位primitiveとして残す。

Graph専用のIShaderRootEncoder、IShaderArgumentScope、GraphRootArgumentScopeも削除した。
既存IRenderEncoder／IComputeEncoder上のSetArgumentValidation一methodだけを必要な橋渡しとする。
prepareはroot依存metadataだけを受け、実行時の全参照を検査するdetached Actionを返す。
GraphはSet中だけprepareを設定しfinallyで解除する。native rootは許可範囲の配列だけを捕捉する
callbackを保持し、RenderGraphContextや入力classを保持しない。通常nativeのnull経路は
callback／scope objectを生成しない。Graph公開encoderに手動SetArgumentTableはなく、
下位nativeのSetArgumentTableは手動記録用に維持する。

このhookはJoinTableが異なるpassのnative tableを共有し得るため必要である。
table一致だけではpassの宣言範囲を保証せず、最新の間接参照はnative command-local copy overlayにある。
Graphにoverlay／command proxyを複製せず、準備時のroot依存と実行時に更新された全transitive依存を検査する。
新public型、friend／InternalsVisibleTo、callback由来を記録する仕組みは追加しない。

参照のcallback間持ち越し自体は禁止しない。Bで取得した参照をAで使う場合も、Aの既存宣言に
native resource・範囲・accessが含まれれば許可する。取得元callback／slot／登録tokenで拒否しない。
Aが未宣言、範囲外、access不一致なら使用時に拒否する。native table affinityは別に検査する。
passごとに一つのimmutable宣言配列とcallbackを準備し、Setごとに追加の宣言配列を作らない。

準備失敗時の以前のroot選択、完了までの世代保持、Graph codec中の再入・End／pipeline変更拒否、
CopyFromの部分更新と要求範囲metadata無効化／元例外を維持する。取得元が異なるがAの宣言に含まれる
cached subrangeの許可、未宣言resourceと更新されたtransitive別範囲の拒否を追加検査した。
API不在guardは旧9caでbuild成功後に旧WriteWireを検出して失敗し、現行の二methodで成功した。

最新sourceの検証:

- Graphics Debug／Release各241成功・失敗0・88skip（329件）。旧offset APIだけのテストは、
  容量超過／未完了total・生成plan不正・API不在・padding／write順の検査へ置換した。
- RenderGraph Debug／Release各133成功・失敗0・skip0。cached B→Aの許可／拒否、
  command-local copy overlay上の間接参照別範囲拒否と回復を検査した。
- 全ホストRelease3,214成功・失敗0・109skip。host consumer／samples buildも成功。
- Vulkan／Wgpu native subset Debug／Release各18成功・失敗0・skip0。
  新prepare拒否後の旧世代readback、Graph自動table binding、同一class root／buffer転送、
  mutable入力変更、table／pipeline／transitive依存とvalidation off/onを含む。
- D3D12管理側Release28成功・Windows native7skip、Browser Release compile成功。
- Windows native／Browser実行・AOT／presentationは未検証。新しい性能測定やzero allocationの主張なし。
- 独立read-only source reviewでblockerなし。API不在guardの旧9ca failureログを保存した。

ログはartifacts/validation11-{debug,graph-debug,fullhost,hardware-debug,hardware-release,d3d-release,browser-release,negative-control,markdown}.log。

以下はinterface統合・cursor導入時の履歴であり、最新writer／Graph APIとは区別する。

## 9ca時点の転送interface統合履歴（2026-11-14）

旧IShaderData markerを削除し、IShaderArgumentsをrootとtyped shader-data bufferの単一契約とした。
4 backend、buffer／slice／factory、Graph／resource宣言、samplesとtest consumerの制約を統一し、
互換aliasは残していない。通常のunmanaged overload／直接byte copyは変更していない。

生成型の同じIShaderArguments classを、実GPUでCopyFrom／CopyBuffer／GPU loadと
SetArgumentsの両方に使い、明示転送値7とSet値13を同時readbackするfixtureを追加した。
同じ型でpublic Graphのbuffer生成・宣言・copyも実行する。手書きcodecも同一classを
rootとupload helperに渡し、別時点の値とpadding／空metadata／encoder未呼び出しを検査する。
API不在guard、single-interface制約、plain unmanagedの非interface経路も検査する。

過去のbenchmark workload／削除済みbinary dataは復元・改変しない。凍結revision専用の
過去harness内の旧marker名は履歴sourceであり、現行consumerとは区別する。
新しい性能測定やAPIの追加flatteningは行わない。

統合後の最終検証:

- Graphics Debug／Release各253成功・失敗0・88skip（341件）。
- RenderGraph Debug／Release各130成功・失敗0・skip0。
- 全ホストRelease3,223成功・失敗0・109skip。host samples／public consumer buildも成功。
- Vulkan／Wgpu native subset Debug／Release各16成功・失敗0・skip0。
  新しい同一classのroot／buffer／Graph fixtureは各backendでvalidation off/onを実行した。
- D3D12管理側Release28成功・Windows native7skip、Browser Release compile成功。
- Windows native／Browser実行・AOT／presentationは未検証。新しい性能測定なし。
- source reviewで本体の変更はmarker削除と制約／実装名の置換だけと確認した。
  buildが生成した.fallout/build.schema.jsonの差分は元のindexへ戻し、CI／build設定は変更しない。

ログはartifacts/validation10-{debug,graph-debug,fullhost,hardware-debug,hardware-release,d3d-release,browser-release,unified-gpu-debug,markdown}.log。

以下は前のcursor-only API変更時の履歴である。

## cursor以外の書き込み経路の削除（2026-11-14）

利用者の明示的な破壊的変更要求により、以前残した互換書き込みを削除した。
IShaderValueWriterは必須WriteWireだけ、IShaderReferenceEmitterは物理順Emitだけとする。
数値だけの型はempty emitterで連続raw bytesをcursorへ渡す。numeric field別の変換は行わない。

削除したAPI／実装:

- SupportsWireCursor、writerのWriteBytes／直接WriteReference、既定throw実装とbulk分岐。
- 手書き／custom getter用bulk fallback、遅延reference fixupとoffset list。
- encoder／ShaderResources／Graphのsnapshot setter・helper、interface既定CaptureとGraphのCapture分岐。
- ShaderRootArguments.CreateSnapshot、encoderなしCreate、Sealのencoder引数。
- ShaderDataLayout.Pack／PackRaw／PackRawInto、IShaderDataLayout.Packとlegacy ShaderReferenceMetadata。
- binding snapshotのCPU Root、snapshot実行admission、backendでのCPU root packing。
- Browser interop／JSのCPU-root upload fallback。managedとJSはsealed native backingを必須にする。

公開consumer／テストの手書きcodecは型付きemitterへ移行した。生成型はcustom／virtual getterも
物理offset順に一度だけ評価し、その前にwireのローカル値コピーを取る。
生成planが不正な場合はgetter／destination書き込み前に毎回ArgumentExceptionで拒否し、
static初期化例外で型をpoisonしない。手書きemitterの不正offsetはcursorの
ArgumentOutOfRangeExceptionで拒否する。既に書いた私有prefixは残り得る。
CopyFromの要求範囲metadata無効化、元例外、Set失敗時の以前の選択は維持する。

残したCaptureはIShaderArguments.CaptureとShaderTransfer.Captureの明示的なCPU検査／診断だけ。
ShaderValueSnapshotをnative書き込みへ戻す入口は存在しない。ModelVertexのlayout probeも
size／Fieldsだけを検査する。ShaderBindingSnapshot.Capture(rootArguments, …)は依存identity収集である。
low-level storage.Write、数値IGraphicsBufferのmemcpy、GPU CopyBufferとremap uploadは必要なprimitiveであり削除しない。

必須API反映・不在を調べるguardは、隔離した旧ac52 checkoutでbuild成功後に
旧writerの4 methodを検出して失敗した。現行の同じguardは成功する。
過去のpacking-onlyテストは削除したAPIの不在・cursor型の検査に置換し、
padding、参照順／16-byte ABI／交差、getter評価回数、partial failure、世代とGPU lifetimeは維持した。

過去3つのbenchmark harnessは凍結revisionで再現する履歴sourceとして明示した。
current HEADのconsumer build対象ではなく、過去C# workload・raw結果は改変していない。
今回のAPI削除に対する新しい性能測定や過去結果の読み替えは行わない。

削除後の最終検証:

- Graphics Debug／Release: 各251成功、失敗0、86skip（337件）。
- RenderGraph Debug／Release: 各130成功、失敗0、skip0。
- 全ホストRelease: 3,221成功、失敗0、107skip。samplesを含むhost consumer buildも成功。
- Vulkan／Wgpu native subset Debug／Release: 各14成功、失敗0、skip0。
- D3D12管理側Release28成功・Windows native7skip、Browser Release compileとJS syntax成功。
- Markdown／diff検査成功。Windows native、Browser実行／AOTとpresentationは未検証。
- sourceの非cursor書き込みsymbol検索はAPI不在assertion／削除を説明するdocs以外0。
  残る参照16-byte encoder、raw buffer copy、remap uploadはcursor下位primitiveである。
- 独立read-only reviewでblockerなし。ModelVertexの不整合例外も元のArgumentException順を維持した。
- 全ホストの一度の実行では全assertion成功後にUI test runnerのPortableSymbolReader.Disposeが
  NullReferenceExceptionとなった。ログを保存し、source／CI設定を緩和せず同じgateを再実行して成功した。

ログはartifacts/validation9-{debug,graph-debug,fullhost,hardware-debug,hardware-release,d3d-release,browser-release,markdown,negative-control}.log。
runnerの障害はartifacts/validation9-fullhost-testplatform-dispose-failure.logに保存した。

以下のcursor導入時の記述／件数は当時のsourceの履歴であり、上記の削除後APIとは区別する。

## ac52時点の同期cursor・backend metadata・helper検証履歴（2026-11-14）

生成codecはraw区間（padding込み）→16-byte参照のinline符号化→次区間を
一つの同期WriteWireで書く。source Span／in参照を呼び出し外に保持しない。
custom getter／手書きcodec／非対応writerと明示Captureは従来の単一呼び出し・
評価順を保持するfallback。normal native backend ownerはidentity列挙だけを保持し、
C# wire offsetの共有listは保存しない。登録slot／range／accessはbindingに必要なため残す。
Setのstable wire encodingは同期実行し、WGSL remapとtransitive admissionはDrawで更新する。

ShaderResourcesのunmanaged Draw／DrawIndexed／Dispatchに残っていた無条件Captureも
除去した。table選択後にgeneric SetArgumentsへ渡す。third-party encoder interfaceの
既定実装、Graphの非対応分岐、ModelVertexの明示layout probe、利用者の明示Captureを
区別する。ShaderBindingSnapshot.Capture(rootArguments, …)はnumeric snapshotではない。

- Graphics Debug／Release: 各275成功、失敗0、88skip（363件）。
- RenderGraph Debug／Release: 各129成功、失敗0、skip0。
- 全ホストRelease: 3,244成功、失敗0、109skip。
- Vulkan／Wgpu native subset Release: 17成功、失敗0、skip0。
- Debugは既存subset15成功とhelper2成功。helper各factはvalidation off/onで
  厳密なsnapshot拒否wrapperを通し、実GPUの1pixel redをreadbackした。
- Cursor coverage／padding／slot順／negative・overflow・交差、handwritten双方順、
  custom getter評価回数、隣接slot、failed Setの旧世代、snapshot失敗解放を検査。
- backend固有identity-only owner、metadata immutable性、元値変更、複数Set、
  transitive／table／pipeline／lifetimeの既存assertionを継続した。
- D3D12管理側Release28成功・Windows native7skip、Browser Release compile成功。
  Windows native、Browser実行／AOT、presentationは未検証。

[最終ABBA比較](performance/wire-cursor-metadata-20261113.md)はbaseline7e89と同じ
harnessで実行した。mapped1要素の中央値7.7%短縮以外は改善を断定しない。
upload allocationは40B／要素減少、interface Setは40B／Set、snapshot Setは256B／Set増加。
完了込みやframe速度の改善は主張しない。helper修正前pilotもsupersededとして保存した。
最終32case、source／binary hash、全raw JSONとcandidate変更sourceを圧縮保存する。

ログはartifacts/validation8-{debug,fullhost,graph-debug,d3d-release,browser-release,hardware-debug,hardware-release,helper-gpu-debug,markdown}.log。

以下は当時のrevisionの履歴であり、最新のcursor・metadata・Set参照符号化時点とは分けて読む。

## 7e89時点のSetArguments最終uniform所有検証履歴（2026-11-13）

Vulkan／D3D12／Wgpu／Browserのgeneric interface・通常unmanaged SetArgumentsを、
Set時点で新しいcommand所有の最終uniformへ直接書く経路に変更した。
Capture、ShaderValueSnapshot、numeric byte配列、pack・padding配列をgeneric経路で
作らない。元のclass／in参照／配置providerも保持しない。明示snapshot入力も
同期的に最終領域へ消費し、互換のためのnumeric cloneは追加しない。
明示Capture API自体は変更していない。

各Setは別世代を所有する。最初の実行でroot参照を符号化してsealし、既存Drawが読む
世代を後のSetや実行で上書きしない。transitive metadata、table照合、Graph groupと
WGSL remapは実行ごとに更新する。失敗したSetは未公開世代だけを破棄し、前回選択と
元の例外を保つ。GPU完了前の資源解放やstaging再利用を許可する変更ではない。

GraphはIShaderRootEncoderの追加contractでreference-onlyの準備・admissionを行う。
非対応の第三者encoderには従来snapshot fallbackを残す。native-rootの
ShaderBindingSnapshotはArguments／RootParameterを使い、legacy Root.Dataの要求は
明示拒否する。隠れたreadbackでnumeric履歴を復元しない。

- Graphics Debug／Release: 各239成功、失敗0、86スキップ（325件）。
- RenderGraph Debug／Release: 各129成功、失敗0、スキップ0。
- 全ホストRelease: 3,208成功、失敗0、107スキップ。
- Vulkan／Wgpuの転送・final-root fixture: Debug／Release各12成功、失敗0、skip0。
- root core24ケースとGraph4ケースで、初回Write／factory counter、Capture拒否codec、
  世代独立、failed Set、範囲・16-byte ABI、scope、transitive更新を確認した。
- 実GPUでSet後の元値変更、複数Setの遅延読取り、pipeline／table guard、
  explicit snapshot配列変更からの独立をreadbackした。既存cancel／dispose／submission
  lifetime検査も全ホストsuiteで継続する。
- D3D12管理側Release: 28成功、失敗0、Windows native7skip。
- Browser Release compile成功。Windows native、Browser実行、AOT、presentationは未検証。

[Set→Dispatch→GPU完了の比較](performance/setarguments-final-root-20261113.md)は
baseline abba2719と同じharnessでABBAを実行した。速度改善は確認していない。
interfaceの1／64回は差を断定せず、explicit snapshotの1回には中央値約5.6%の
遅延増加を観測した。managed allocationは全caseで減った。これをframe速度や
allocation-freeとは扱わない。全16case、source／binary hash、raw JSONを保存する。

ログはartifacts/validation7-{initial,fullhost,graph-debug,d3d-release,browser-release,hardware-debug,hardware-release,markdown}.log。

21:56のcursor形式の検討は、C# raw segmentと16-byte参照を一つの同期関数内で
書く追加生成contractとして実現可能である。ただし現在の全wire WriteBytesの
Spanを呼び出し後まで保存せず、新しいWriteWire＋同期reference emitterの形が
必要になる。offset順・overlap・getter評価順・手書きfallbackを決め、numericの
個別シリアライズや無効な診断reflectionを復活させない。Browserのchunkごとの
interop費用も未測定である。今回のSetは参照解決を最初のDrawまで遅延する境界を
維持し、この追加protocolを無断で混ぜていない。詳細は最新ADR0035へ記録した。

以下は旧revision時点の履歴であり、最新Set契約とは分けて読む。

## abba2719時点のreference-only CopyFrom検証履歴（2026-11-13）

20:36の承認により、IShaderDataSourceの完全なCPU値Read契約を廃止し、
ReadReferences／SetTransferredReferencesとimmutableなShaderReferenceMetadataへ移した。
ShaderBindingSnapshot.Elements、Graph、copy overlay、任意検証のrevision照合も
reference-onlyである。Data、numeric byte配列、元の値objectや配置providerは保持しない。

参照付きCopyFromはgeneric writerで現在のprivate unmanaged backingを同期的にmapped
uploadへ書く。四つのbackendからIShaderArguments.Capture、要素ごとの
ShaderValueSnapshot、numeric byte配列とtransactional batch scratchを削除した。
残したのはbindingに必要なresource identityとwire slotのmetadataだけである。
明示Capture／SetArgumentsのvalue snapshotは変更していない。GPU-only bufferからの
隠れたreadbackや待機も追加していない。

失敗atomicityは要求しない。codec、encoder、mapped書き込みが途中で失敗すると、
byteは部分更新され得る。要求したmetadata範囲は全て無効とし、範囲外を変更せず、
元の例外を返す。無効なsourceからのtyped copyをnative記録前に拒否する。
全範囲の再転送で回復し、記録済み・in-flight資源の寿命と同期責任は維持する。

直接方式とCPU一時buffer方式は同じmetadata義務で、1／64／1024要素の実際の
CopyFromをBDNで比較した。CPU一時bufferにはsmall CPU stageで5–6%の利益も
あったが、completed GPU copyでは1–2%の差で断定できず、1024要素では約13%遅く
allocationも増えたため、直接方式を採用した。pooled scratchや他backendの性能に
一般化はしない。最終candidateのABBA、全24case、pilotを含む全証拠は
[比較report](performance/shader-upload-strategies-20261113.md)に保持する。
従来のcab86fd3の6.45倍／2.24倍の結果は別の歴史的比較であり、流用しない。

- Graphics Debug／Release: 各215成功、失敗0、84スキップ（299件）。
- RenderGraph Debug／Release: 各125成功、失敗0、スキップ0。
- 全ホストRelease: 3,180成功、失敗0、105スキップ。
- Vulkan／Wgpu転送fixture: Debug／Release各10成功、失敗0、スキップ0。
  最新値、失敗後のmetadata拒否と再転送による回復を実readbackで確認した。
- failure／diagnostic15ケースで、元の例外、要求範囲の無効化、host bounds／overflow、
  不透明ABI、reference-only性、手書きclass／structの任意診断を確認した。
- D3D12管理側Release: 28成功、失敗0、Windows nativeスキップ7。
- Browser Release compile成功。Windows native、Browser実行、AOT、presentationは未検証。

ログはartifacts/validation6-{Debug,Release,fullhost,graph-debug,d3d-release,browser-release,hardware-Debug,hardware-Release,markdown}.log。
新decisionとbreaking API移行はADR0035だけへ反映し、過去ADRは変更しない。
以下は各旧revision時点の履歴であり、今回の最新契約ではない。

## 693b969f時点の転送境界・scratch検証履歴（2026-11-13）

「転送前の変更はすべて有効」という要件について、cab86fd3の実装を先に追跡し、
変更前に新しいGPU readback検査を実行した。buffer／slice作成、tableへの登録、
RenderGraphのimport／Use／CompileでCPUの数値やreference classを早期captureする
不具合は見つからず、既存の明示転送時点で最新値を読むことを確認した。

| 操作 | 値を読む時点と所有権 |
| --- | --- |
| buffer・slice作成、table登録 | allocation／範囲／参照identityのみ。CPU値を固定しない |
| RenderGraph import・Use・Compile | resource宣言と実行callbackを保持。callback内の値はまだ転送しない |
| mapped stagingのCopyFrom | この呼び出しで現在のCPU値・参照を同期的に読み、独立したstagingとmetadataへ明示転送する |
| commandのCopyBuffer | 元のCPU objectではなく、指定したstaging範囲をGPUへcopyするcommandを記録する |
| SetArguments | このAPI自体がroot値の明示設定。後続Draw／Dispatchは設定時の値を使う |
| Capture | 呼び出し側が明示的に要求した独立CPU snapshot。元の値を借用し続けない |

したがって、CopyFromより前のCPU変更はすべて含まれる。CopyFrom後に元のarrayや
reference classを変更しても、既に書いたstagingは変わらない。新しい値を転送するには
再びCopyFromする。CopyBuffer記録前の、mapped stagingへの複数の明示書き込みは
最後の値が有効になる。一方、記録済み・実行中のGPU copyが読むstagingを無断で
書き換えることや、GPUが実際に読む瞬間までCPU objectを自動追従することは意味しない。
Span／stackのin参照を非同期処理まで保持する実装にもしていない。

変更前の検査では、numeric arrayとgenerated reference classの数値・参照を
宣言／登録後にAからBへ変更し、Vulkan／Wgpuの検証有効／無効で最新Bが読み出せた。
元のCPU値をCopyFrom後に変更しても最初の転送結果はBのままであり、次の明示転送で
新しい値へ変わることも確認した。これはstale captureの修正を捏造するための
old-fail検査ではなく、既存の境界を保護するcharacterizationである。
Graphの別検査ではCompile後のcallbackでstaging metadataを変更し、copy記録時の
値がcommand-local overlayへ入ることを確認した。これはfake metadata検査であり、
実stagingをGPU使用中に上書きしてよいという検査ではない。

ただし、四つのbackendのshader-data CopyFromには、各要素で新しいpacked arrayを
作り、それを既存のbatch scratchへ再copyする冗長な中間段階があった。
PackRawIntoでowned scratchの該当Spanへ直接packするよう変更した。
CPU snapshotと参照identityは保持し、capture-all-before-pack、batch全体のpack成功後に
native stagingへ書く順序とmetadata公開を維持する。snapshotとのaliasを拒否し、
参照slotの範囲・重複・16-byte ABI検査も残す。scratchはencoding失敗時に部分更新され得るが、
成功前にnative stagingへ公開しない。配置reflectionや診断状態を無効経路へ追加しない。

最終検証:

- Graphics Debug／Release: 各200成功、失敗0、84スキップ（284件）。
- RenderGraph Debug／Release: 各125成功、失敗0、スキップ0。
- 全ホストRelease: 3,165成功、失敗0、105スキップ。
- 実GPUの境界／unmanaged転送fixture: Debug／Release各10成功、失敗0、スキップ0。
- Direct3D12管理側Release: 28成功、失敗0、Windows nativeスキップ7。
- 変更前の境界GPU fixture: 3成功、失敗0、スキップ0。metadata graph検査も成功。
- scratch専用18ケースで、バイト列・padding、外側guard、reference identityと順序、
  alias／サイズ／不正slot拒否、snapshotの独立所有権を検査した。

ログはartifacts/validation5-{before-debug,before-hardware,graph-before,debug,fullhost,graph-debug,d3d-release,hardware-debug,hardware-release,markdown}.log。
転送APIのdecisionを変更していないためADR0035も過去ADRも書き換えない。
benchmark-only branch b2ca7c92の測定はcab86fd3に固定した過去結果であり、この
scratch変更後の速度を測ったものではない。CPU captureの約33µs／1024要素という
独立stageの測定を、すべての転送の費用とも解釈しない。

## main同期時の検証履歴（2026-11-13）

同期元mainは70db1cd8144c22256505a433531efb0e37a4a7d0、同期前のPR先端は
7e2c3208d7de7c3e8c5498bbac86fc217b511f08である。開始済みの通常mergeを
引き継ぎ、conflictのない統合結果を検査した。SDKはglobal.jsonの10.0.403を
維持し、既存activationで.NET runtime10.0.14を確認した。
以降の旧セクションは各revision時点の履歴であり、最新結果ではない。

mainのColor2D廃止とSRgba／LRgba導入は、Drawing2D／UIの明示的な
linear・premultiplied変換境界を維持する。実際のshader wireは引き続きVector4などの
既存numeric構造体であり、generic色型の内部表現をshader float4と暗黙に同一視しない。
追加したColorShaderTransferTestsは、`SRgba<float>`／`LRgba<float>`がcodecや
IShaderArgumentsを必要とせず、C#の実バイト列・strideのままcapture／明示copyを
行うことを検査する。色変換は描画側の明示処理であり、raw転送には混入しない。
生成されたreference classのprivate numeric backing、任意検証のDebug／Release既定無効、
無効経路で診断状態を作らない契約は変更しない。

mainの決定的テスト方針に合わせ、PR側に残っていたGC割当量と100回ループの
判定を廃止した。代わりに最初の実行でcollectionのCount参照・列挙と登録probeが
すべて0であることを直接確認する。mainで削除されたwarmup／JIT依存の割当量テストは
復活させず、必要な所有権・弱参照の検査は維持する。

公式Fallout runnerによる結果:

- 全ホストRelease: 3,145成功、失敗0、103スキップ（3,248件）。
- Graphics Debug／Release: 各181成功、失敗0、82スキップ（263件）。
- Direct3D12 Debug／Release: 各28成功、失敗0、Windows nativeスキップ7（35件）。
- Markdown・差分の空白検査は成功。mergeで入ったmainのschema変更は保持し、
  runner由来のworktree差分だけをindexへ戻す。

同じRelease成果物へ、tests/README.mdで許可された直接実行のfilterを使い、
転送fixtureだけを実GPUで追加検査した。11成功、失敗0、スキップ0である。
この11件にはCPU検査も含み、Vulkan／WgpuのVector3転送と、新しい色型の
検証有効／無効でのraw copyを含む。driver discoveryはRadeon8060S Graphics、
RADV、Mesa26.0.8-1ubuntu0.3で成功した。presentationや全GPU suiteは再実行していない。

    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Release
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet test tests/Graphics/Lumyte.Graphics.Tests --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~UnmanagedShaderTransferTests|FullyQualifiedName~ColorShaderTransferTests' --logger trx --results-directory artifacts/test-results
    dotnet run --project build -c Release -- --target CheckMarkdown

ログはartifacts/validation4-{fullhost,graphics-debug,d3d-debug,d3d-release,transfer-hardware,vulkaninfo,markdown}.logに
保持する。Windows実D3D12、Browser実WebGPU、NativeAOT、物理画面／presentationの
検証は今回の範囲外であり、compileやbuffer copy成功から推論しない。
過去Graphics ADR0009/0015/0018/0022/0023はef61258fと同一のまま維持する。

## 同期前の任意検証レイヤの検証履歴（2026-11-13）

以下は777a7e11後、main同期前の任意検証レイヤを対象とする履歴である。
再レビューで見つかったD3D12のnative device removal照会も、Lost状態を許す
QueueNative参照へ修正した。これにより喪失後のretirement確認と元HRESULTの保持を妨げない。
Windows nativeでの実行確認は未実施であり、管理側テストとは区別する。
後続の節はそれぞれの変更時点の結果であり、Debug限定の旧検証方針は
明示的なdevice単位の有効化へ置き換わっている。

具体的なバックエンドのdeviceを生成した直後、子リソースを生成する前に
EnableValidation()を呼ぶ。Debug・Releaseとも既定では無効であり、
IGraphicDevice.ValidationEnabledで状態を確認できる。D3D12自身の
EnableDebugLayerとは別の、Lumyteの管理側契約に対する診断である。

- 有効時: 記録済み・実行中リソースの寿命、マッピング競合、親子関係、
  記録時とSubmit時の登録失効、範囲別バッファアクセス、mip/layer別の
  テクスチャ状態、コマンドをまたぐ依存、バリアの適用範囲、
  Acquire/Submit/Presentのバイナリセマフォ、転送メタデータ順序を追跡する。
- 無効時: nullable分岐とdeviceの生成状態フラグ以外の診断用追跡を作らない。
  診断用コールバック、状態再生、生成フィールド表、配置検証用リフレクションは
  実行しない。ホストメモリの範囲・オーバーフロー検査、必要なbinding情報、
  ネイティブエラー処理は残す。
- GPU内部の分岐・動的インデックス、直接のネイティブ操作、unsafeによる
  stagingバイト列の書き換えは観測できない。アクセスは登録範囲と宣言した
  ステージに基づいて保守的に扱う。実行の一部がネイティブへ発行された後の
  失敗は巻き戻せず、非同期失敗では追跡したキュー状態を不明として扱う。

公式Fallout runnerによる検証:

    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    dotnet run --project build -c Release -- --target CheckMarkdown

- 全ホストRelease: 2,972成功、失敗0、101スキップ。
- Graphics Debug/Release: 各179成功、失敗0、80スキップ。
- 実GPU Graphics Release: 255成功、既知のVulkan深度形式失敗2、
  native presentationの2テストをスキップ。有効化したVulkan/Wgpu転送は成功。
- Direct3D12の管理側Releaseコンパイルは成功。Windows実機は未検証。
- Markdown検査、差分の空白検査は成功。

native presentationは実行成功を主張しない。所有するWeston/Xwaylandでの
検証を試みたが、Lumyte起動前のcompositor終了時クラッシュと、独立した
Xwaylandのキーボード初期化失敗で有効な表示環境を確立できなかった。
対応するVulkan/Wgpuテストは追加済みだが、動作する表示環境なしでは
スキップする。Browser実行、Windows D3D12、NativeAOT、全ホストDebugも
今回の実行範囲には含まない。

実行ログはartifacts/validation2-{debug,fullhost,hardware,d3d,markdown}.logと
同じartifacts内のcompositor/Xwaylandログに保持する。
過去ADR0009/0015/0018/0022/0023はef61258fと同一のまま維持する。

## D3D12部分提出の所有権修正（2026-11-13）

wait・signalを個別に発行するD3D12経路では、最後のcompletion signalだけを
成功境界にすると、途中まで成功した操作を失う。各native wait／signalが
成功した直後に、その操作だけを同じcompletion ownerへ反映するよう修正した。
未発行・失敗した操作を成功として公開せず、commandsが空でも、発行済み操作が
あれば回復用ownerをdeviceへ保持する。producerの完了だけではconsumer waitの
使用中状態を退役させない。

部分発行後はqueue状態を不明として扱う。元の例外をそのまま返し、回復用markerの
発行失敗やpending fenceのDispose拒否で上書きしない。回復用markerはGPU待機を
強制せず、実際の完了観測まで依存を保持する。必要な場合の
WaitForRetirementAsyncは明示的な回復待機、またはnative device removalの
確認後にだけownerを退役させる。キャンセルや早期Disposeは完了の代わりにならない。
最初のnative操作より前の失敗では、未発行fence専用の解放経路を使う。

通常の無効経路に診断用collectionやcallbackは追加しない。部分発行時のnative
cleanup所有権は既存submissionと既に作成したdescriptor配列、deviceのlinked ownerで
維持し、失敗後のowner保存にも新しいlistやnodeを確保しない。native transportの
内部seamにより、公開deviceをforwarding wrapperにせず実際のqueue経路を検査する。

管理側のnative-call fault injectionによる24ケースを追加した。第2wait、第2signal、
最終completion signal、semaphore-only提出、最初のwait／signal／executeの失敗、
元の例外identity、未発行signalの非公開、producer完了後のconsumer保持、キャンセル、
実完了または確認済みdevice removalでの退役を有効／無効の両方で検査する。
既存のD3D12 parityテストに残っていたDebug限定期待値も、明示的な診断呼び出しが
両構成で同じ契約になるよう揃えた。

- 旧来の「最後に一括公開・commandsだけを見て保持」のqueue処理を同じnative seamで
  一時的に復元したnegative control（HRESULT境界2ケースの追加前）は、
  失敗19、成功7、Windows nativeスキップ7。
  修正済みsourceへ戻したことを確認してから最終検証を実行した。
- Direct3D12 Debug／Release: 各28成功、失敗0、Windows nativeスキップ7（35件）。
- Graphics Debug／Release: 各179成功、失敗0、80スキップ。
- 全ホストRelease: 2,972成功、失敗0、101スキップ。
- Windowsの実D3D12実行は引き続き未検証。fault injectionはnative GPU命令を
  発行しない管理側のqueue／semaphore／submission経路の検証である。

実行コマンドは同じ公式runnerのTest targetへ、
project=tests/Graphics/Lumyte.Graphics.Direct3D12.Testsと
configuration=Debug／Releaseを渡す。ログは
artifacts/validation3-{d3d-debug,d3d-release,graphics-debug,fullhost,negative-control}.logに
保持する。negative controlのsourceと復元用sourceもignored artifacts内だけに保存し、
実装・履歴には取り込まない。

## Registration-owned access follow-up (2026-11-13)

Starting at 4e58e198, raw-buffer WriteBuffer selects ShaderBufferAccess.ReadOnly
(default) or ReadWrite at registration. Usage flags remain allocation capabilities;
registration validates the selected capability. Reference-containing ShaderDataSlice
registrations remain read-only. IGpuRef stays opaque and unchanged, and GetElement
follows the original immutable registration. Replacement creates new metadata;
old references are never executed after replacement. ADR0014's caller-owned lifetime
contract is unchanged: no generations, invalidation tracking or lifetime rescans.

WriteReference now receives only the C# offset and reference. ShaderValue has no
runtime Path, the generator emits no reference-path argument, and Pack callbacks
no longer receive shader kind strings. Wgpu/Browser classify reachable references
from registration access, including transitive references. Separate read/write
registrations of the same range use separate pool/remap lanes, without accidentally
filling both lanes from the allocation's capabilities. D3D12 selects SRV/UAV by
registration; Vulkan physical addresses and D3D12 absolute-offset encoding remain
unchanged. Shader resource kind/type/access consistency is a Debug diagnostic,
not a Release admission rule. Host slot bounds, duplicate offsets, reference ABI
length, selected-table membership and registration capability checks remain active.

ArgumentTableExercise now checks read-only/writable capabilities, invalid enum values,
GetElement access, distinct registrations of one allocation and replacement metadata.
It runs through the existing Vulkan/Wgpu hardware argument-table tests and Windows
D3D12 parity exercise. A deterministic CPU theory checks read/write pool deduplication
and each remap lane for two aliased ranges, plus deliberately reversed shader access:
Debug diagnoses the mismatch; Release still uses registrations for classification.
No intentionally invalid shader root is submitted to the GPU.

Use the activation/TMPDIR setup below and the official Fallout runner:

    dotnet run --project build -c Release -- --target Test --host-only --configuration Release
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Debug
    LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.RenderGraph.Tests --configuration Release
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Debug
    dotnet run --project build -c Release -- --target Test --project tests/Graphics/Lumyte.Graphics.Direct3D12.Tests --configuration Release
    dotnet run --project build -c Release -- --target CheckMarkdown

Final targeted results:

- Hardware Graphics Debug and Release: each 167 passed, 2 failed, 0 skipped
  (169 total). Only the known VulkanAdvancedCommandsAsync and
  VulkanUncachedAdvancedCommandsAsync unsupported depth-format paths fail.
  The registration capability/GetElement/replacement checks and new alias/remap
  theory pass; no native assertion or GPU-test eligibility was relaxed.
- Full host Release: 2,886 passed, 0 failed, 97 skipped (2,983 total,
  40 test projects). Official restore/compile/test all succeeded; compile reported
  0 warnings/errors. No full-host Debug run was performed.
- Official CheckMarkdown: 311 files, 0 issues; git diff --check passed.
- RenderGraph Debug and Release: each 124 passed, 0 failed/skipped.
- Direct3D12 Debug and Release Linux CPU tests: each 4 passed, 0 failed,
  7 Windows-native tests skipped. Managed backend source compiled in both modes.
- Drawing Debug and Release CPU tests: each 12 passed, 0 failed,
  10 native GPU tests skipped. Null references still fail host snapshot capture
  in both modes; shader-schema checks alone are Debug-only.

The initial host compile exposed remaining callback fixture signatures and test
