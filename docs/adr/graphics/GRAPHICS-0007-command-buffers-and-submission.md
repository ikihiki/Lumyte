# ADR-GRAPHICS-0007: CommandBufferと明示的なGPU実行

- 状態: 置換済み（実行時検証の方針のみ。その他の決定は引き続き採用）
- 日付: 2026-10-09
- 変更日: 2026-10-10
- 置換範囲: 記録resourceの寿命・mapping再検証、subresource状態表、使用中の解放拒否。利用者が寿命と同期を管理し、検証のための追跡費用を省く方針へ変更する。
- 後継: [ADR-GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)。以下の本文は判断時点の記録であり、上記の実行時検証には後継を適用する。

## 背景

GPUコピー、resource barrier、render／compute passを、resource自体のAPIから分離して記録する必要がある。CPUのCopyFrom／CopyToとGPU転送、記録とsubmit、submitと完了待機を別の操作にする。

[buffer](GRAPHICS-0002-typed-buffers.md)、[texture](GRAPHICS-0003-textures-and-views.md)、[Argument Table](GRAPHICS-0005-argument-tables-and-gpu-references.md)、[shader](GRAPHICS-0006-shader-compilation-and-modules.md)の契約へ接続する。

## 決定

### 責務と初期範囲

backendの具象classがIGraphicsCommandBuffer、IGraphicsQueue、IGraphicsSubmissionと各encoderを直接実装する。共通の公開契約とDescはGraphics.Abstractionsへ配置する。device生成を抽象化しない。

初期範囲はdeviceが所有する一つのgraphics／compute／copy対応queue、one-shot command buffer、明示的なbarrier、buffer／2D color textureのGPUコピー、clearとstoreを行うcolor render pass、compute passの開始・終了である。複数queue、queue ownership transfer、secondary command、command再利用、timestamp、depth/stencil attachment、MSAA resolveは対応するresource／commandの拡張で扱う。

command bufferの作成直後はRecording。利用者がcopy／barrier／passを記録し、FinishでExecutableへ移行する。queueへSubmitすると初めてGPU実行を開始する。同じcommand bufferを再submitしない。Submitは待機せず完了handleを返し、WaitAsyncは指定したsubmissionの完了だけを待つ。

resourceのCPUコピー、staging確保、map／unmap、GPUコピー、barrier、submitと待機を互いに呼び出さない。記録APIはGPU命令を保存するだけで、CPUデータのコピーや実行完了待機を行わない。

### 公開API

比較元はorigin/main。説明と失敗条件をコメントで示す。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicDevice
     {
+        // device所有の唯一のgeneral queue。利用者がqueueをDisposeするAPIは設けない。
+        IGraphicsQueue Queue { get; }
+        // Recording状態で返す。native encoder／allocator等はbackendが所有する。
+        IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc);
+        // textureコピーに必要なbyte単位の値。paddingやstagingを確保しない。
+        TextureCopyLayout GetTextureCopyLayout(TextureFormat format);
     }
+    public sealed record CommandBufferDesc
+    {
+        public string? Label { get; init; }
+    }
+    public enum CommandBufferState { Recording, Executable, Submitted, Completed, Faulted, Disposed }
+    public interface IGraphicsCommandBuffer : IDisposable
+    {
+        CommandBufferState State { get; }
+        // sourceとdestinationのbyte lengthは等しい。異なるTでもraw bytesだけをコピー。
+        // bufferは別allocation、CopySource／CopyDestination、unmappedでなければならない。
+        void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
+            where TSource : unmanaged where TDestination : unmanaged;
+        // 各regionは同じformat・extent。filter／変換／mip生成はしない。
+        void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination);
+        void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination);
+        void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination);
+        // pass外で明示的に記録する。buffer／texture自身へbarrier APIを追加しない。
+        void Barrier(MemoryBarrierDesc barrier);
+        void Barrier<T>(BufferBarrierDesc<T> barrier) where T : unmanaged;
+        void Barrier(TextureBarrierDesc barrier);
+        IRenderEncoder BeginRenderPass(RenderPassDesc desc);
+        IComputeEncoder BeginComputePass(ComputePassDesc desc);
+        // active passがある場合はInvalidOperationException。GPUへsubmitしない。
+        void Finish();
+    }
+    public interface IGraphicsQueue
+    {
+        // 同一deviceのExecutable bufferをlist順で、一度だけsubmitする。
+        // empty、重複buffer、別device、Recording／Submittedは拒否する。
+        IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers);
+    }
+    public enum SubmissionStatus { Pending, Completed, Failed, Disposed }
+    public interface IGraphicsSubmission : IDisposable
+    {
+        // 非blockingの状態確認。shaderの実行結果やresource bytesは読み出さない。
+        SubmissionStatus Status { get; }
+        // submissionだけを待つ。キャンセルは待機だけを中止し、GPU仕事を取り消さない。
+        ValueTask WaitAsync(CancellationToken cancellationToken = default);
+        // pending中のDisposeは拒否。GPU完了待機をDisposeへ隠さない。
+    }
+    public sealed record TextureCopyLayout
+    {
+        // 初期の非圧縮color formatのtexel size。allocation layoutではない。
+        public required uint BytesPerTexel { get; init; }
+        public required ulong BufferOffsetAlignmentInBytes { get; init; }
+        public required uint BytesPerRowAlignment { get; init; }
+    }
+    public sealed record TextureCopyRegion
+    {
+        public required IGraphicsTexture Texture { get; init; }
+        public uint MipLevel { get; init; }
+        public uint OriginX { get; init; }
+        public uint OriginY { get; init; }
+        public uint BaseArrayLayer { get; init; }
+        public required uint Width { get; init; }
+        public required uint Height { get; init; }
+        public uint ArrayLayerCount { get; init; } = 1;
+    }
+    public sealed record BufferTextureCopyLayout
+    {
+        // BufferSlice<byte>のoffsetがnative copyの開始位置。二重offsetを設けない。
+        public required BufferSlice<byte> Buffer { get; init; }
+        public required uint BytesPerRow { get; init; }
+        public required uint RowsPerImage { get; init; }
+    }
+    [Flags]
+    public enum PipelineStage
+    {
+        None = 0, Host = 1, Copy = 2,
+        VertexShader = 16, FragmentShader = 32, ComputeShader = 64, ColorOutput = 128,
+        AllGraphics = VertexShader | FragmentShader | ColorOutput,
+        AllCommands = Copy | AllGraphics | ComputeShader,
+    }
+    [Flags]
+    public enum ResourceAccess
+    {
+        None = 0, HostRead = 1, HostWrite = 2, CopyRead = 4, CopyWrite = 8,
+        ShaderRead = 64, ShaderWrite = 128,
+        ColorRead = 256, ColorWrite = 512,
+    }
+    public readonly record struct BarrierScope(PipelineStage Stages, ResourceAccess Access);
+    public sealed record MemoryBarrierDesc
+    {
+        public required BarrierScope Before { get; init; }
+        public required BarrierScope After { get; init; }
+    }
+    public sealed record BufferBarrierDesc<T> where T : unmanaged
+    {
+        public required BufferSlice<T> Buffer { get; init; }
+        public required BarrierScope Before { get; init; }
+        public required BarrierScope After { get; init; }
+    }
+    public enum TextureState { Undefined, CopySource, CopyDestination, Sampled, ColorAttachment }
+    public readonly record struct TextureSubresourceRange(
+        uint BaseMipLevel, uint MipLevelCount, uint BaseArrayLayer, uint ArrayLayerCount);
+    public sealed record TextureBarrierDesc
+    {
+        public required IGraphicsTexture Texture { get; init; }
+        public required TextureSubresourceRange Range { get; init; }
+        public required TextureState BeforeState { get; init; }
+        public required TextureState AfterState { get; init; }
+        public required BarrierScope Before { get; init; }
+        public required BarrierScope After { get; init; }
+    }
+    public enum AttachmentLoadOp { Load, Clear }
+    public enum AttachmentStoreOp { Store, Discard }
+    public readonly record struct ClearColor(double Red, double Green, double Blue, double Alpha);
+    public sealed record RenderColorAttachmentDesc
+    {
+        // 同一device、RenderAttachment usage、D2、単一mip／layerのviewを指定する。
+        public required IGraphicsTextureView View { get; init; }
+        public required AttachmentLoadOp LoadOp { get; init; }
+        public required AttachmentStoreOp StoreOp { get; init; }
+        public ClearColor ClearValue { get; init; }
+    }
+    public sealed record RenderPassDesc
+    {
+        public string? Label { get; init; }
+        // slot順。初期範囲は1以上MaxColorAttachments以下。各attachmentの寸法を揃える。
+        public required IReadOnlyList<RenderColorAttachmentDesc> ColorAttachments { get; init; }
+    }
+    public sealed record ComputePassDesc
+    {
+        public string? Label { get; init; }
+    }
+    public interface IRenderEncoder
+    {
+        // passを一度だけ終了する。encoderは以後利用不可。command bufferはRecordingへ戻る。
+        void End();
+    }
+    public interface IComputeEncoder
+    {
+        void End();
+    }
 }
```

### 記録と状態遷移

command bufferの生存中は同時に一つのpassだけを持てる。BeginRenderPass／BeginComputePassはRecordingかつpassなしの場合だけ許可する。active passがある間はcommand bufferのcopy・barrier・次のpass・Finishを拒否する。encoderは自分のpass内だけで命令を記録し、End後の操作と二度目のEndを拒否する。

Finishはnative command bufferを確定してExecutableにする。Finish後は記録不可。Submit成功後はSubmitted、一つのsubmissionが完了すると含まれるcommand bufferはCompletedとなる。Recording／Executable／CompletedのDisposeはnative resourceを解放し、未submitの記録を破棄できる。active passがあるDisposeは拒否するため、破棄する場合も先にEndする。Submitted中のDisposeは拒否し、完了待機を行わない。Disposeはidempotent。

通常の入力検証に失敗した記録操作は、command状態を変えずnative命令を追加しない。native記録の確定に失敗した場合はFaultedへ移し、Finish／Submitを拒否する。未submitでGPU使用がないFaultedは破棄できる。範囲・数値の不整合はArgumentException／ArgumentOutOfRangeException、状態違反はInvalidOperationException、別deviceはArgumentException、解放済みresourceはObjectDisposedException、非対応featureはNotSupportedException、native失敗は診断を含むInvalidOperationExceptionとする。

### GPUコピー

CopyBufferは両sliceのOffsetInBytes／SizeInBytesを使い、copy lengthが等しいことを検証する。要素数ではなくbyte数の等価性が基準で、Tの変換やlayout変換はしない。両bufferのcopy alignmentをbyte換算後に検証し、CountやSizeInBytesを補正しない。同一allocation間のコピーは初期portable契約で拒否し、scratch bufferを自動確保しない。sourceはCopySource、destinationはCopyDestination usageを持ち、両方ともunmappedでなければならない。

CopyTextureはmip寸法、origin＋extent、layer範囲とusageを検証する。sourceとdestinationは同じ正確なformatとextentで、異なるallocationを使う。初期範囲は既存の非圧縮2D color format・single sampleのみ。viewではなくallocationとregionを渡し、copy専用textureでも使える。filter、色変換、mipmap生成、空rangeは扱わない。

bufferとtexture間のcopyはGetTextureCopyLayoutの必須alignmentとtexel sizeを使う。BytesPerRowは `Width * BytesPerTexel` 以上かつalignmentの倍数、RowsPerImageはHeight以上とする。`BufferSlice<byte>`のoffsetはBufferOffsetAlignmentInBytesの倍数。必要byte長はchecked arithmeticで次のように計算する。

```text
(ArrayLayerCount - 1) * RowsPerImage * BytesPerRow
    + (Height - 1) * BytesPerRow + Width * BytesPerTexel
```

必要byte長がslice内に収まることを検証し、行末・layer間paddingは利用者が確保する。paddingの追加、row repack、staging確保やCPUコピーをcommandへ隠さない。copyは指定texelだけを書き、paddingやregion外の内容を転送対象にしない。

GPU参照を含むbufferのshader dataとCPU依存metadataの伝播・GPU書き込みによる無効化は、参照とshader dataの連携設計で扱う。commandの連携点はcopyのsource／destination byte範囲と記録順序であり、Bufferクラスへコピー命令やbarrier責務を戻さない。

### Barrierとresource state

barrierは利用者がcommand bufferへ記録する。BufferBarrierは指定byte範囲のexecution／memory dependency、TextureBarrierは指定subresourceのdependencyとstate遷移、MemoryBarrierはresourceを限定しないdependencyを示す。初期契約ではpass外で使い、pass間に必要なbarrierがある場合はEndしてから記録する。

stage／access flagの未知bit、相互に対応しないstageとaccess、usageに反するstate、空subresource範囲を拒否する。textureのAfterStateへUndefinedは指定できない。BeforeStateのUndefinedは旧内容を保持しない意味であり、以前のGPUアクセスに対する同期が不要になる意味ではない。再使用時の依存scopeも利用者が指定する。

native APIが明示barrierを持つ場合は対応する命令へ変換する。native APIが状態遷移を管理する場合も、宣言したdependencyとusageを検証し、そのAPIの順序・visibility保証で実現する。範囲をnative APIが要求する粒度へ広げることはできるが、転送データの範囲やresource sizeは変更しない。

textureはcommand buffer内での最初の利用前に、利用者が明示的なTextureBarrierを記録してstateを宣言する。記録済みのstateと後続のBeforeStateの不一致を拒否する。command開始時の実際のstateを推測せず、利用者が指定する。

backendは記録済みの宣言を整合性検証に使えるが、resourceのGPU内容から依存を推測したり、不足したbarrierを補完したりしない。command間・submission間の実際のBeforeStateと依存は利用者が管理する。queueの実行順、submissionの完了、resourceのmemory visibilityは同一の概念として扱わない。

### Render／compute pass

render attachmentは同一deviceの生存viewで、RenderAttachment usage、D2、単一mip・単一layerを必要とする。選択mipの幅・高さが全slotで一致し、重複または重なるsubresourceを複数slotへ指定しない。format・寸法はviewから取得し、利用者に別のattachment layoutを再指定させない。初期texture契約はsample count 1であり、MSAAとresolveはtexture拡張とともに追加する。

Loadは既存内容を読み、ClearはfiniteなClearValueで初期化する。Discard後の内容は未定義で、後続のread／Loadのために保持されない。passはattachmentをColorAttachment stateで使用し、前後のbarrierを自動記録しない。BeginRenderPassではDescとlistをsnapshotにし、呼び出し後の元list変更が記録へ影響しない。

compute passは命令記録のscopeを表し、初期範囲では開始とEndを提供する。両encoderはEnd後に利用できず、次のpassは新しいhandleを返す。

### Submitと完了

Submitはlist全体を検証してsnapshotにし、同一queueへその順番で提出する。通常の入力不備で部分submitしない。native提出の失敗は診断を含む例外として報告する。native APIが提出されていないことを保証する場合だけExecutableを維持する。device lossでGPU使用終了を確定できる場合はFaultedとして再submitを拒否する。

submissionはそのsubmitに含まれる全command bufferの完了を表す。後続のsubmitまでqueue全体をidleにするAPIではない。WaitAsyncは必要なnative event処理を進められるが、追加のsubmitやresourceのmap／readbackを行わない。完了失敗はStatus=Failedとして診断を含む例外を返す。

WaitAsyncのキャンセル後もsubmissionはPendingのまま進行し、利用者は後から再度待機または状態確認できる。Pending中のsubmission／command bufferのDisposeは拒否する。Completedのsubmissionを解放してもcommand bufferは自動解放しない。Failedの解放はGPU使用が終わったことをbackendが確定できる場合だけ許可する。

### Upload／readbackの利用手順

uploadは利用者がUpload bufferを確保し、MapAsync、CopyFrom、Unmapを行う。次にcommand bufferへ必要なbarrierとCopyBuffer／CopyBufferToTextureを記録し、後続利用へのbarrierを記録してFinish／Submitする。stagingはそのGPU使用の完了まで生存させる。

readbackは利用者がReadback bufferを確保し、commandにsourceのdependency、CopyBuffer／CopyTextureToBuffer、必要なHostRead向けdependencyを記録する。Finish／Submit後にWaitAsyncし、Readback.MapAsync、CopyTo、Unmapを行う。ReadBufferAsyncやReadStagingAsyncのようにこれらを一括実行するAPIを設けない。

```csharp
// sourceとreadbackは同じbyte長のbuffer。必要なbarrierは利用側が記録する。
using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
commands.CopyBuffer(source.Slice(0, source.Count), readback.Slice(0, readback.Count));
commands.Finish();
using IGraphicsSubmission submission = device.Queue.Submit([commands]);
await submission.WaitAsync();
await readback.MapAsync();
readback.CopyTo(destination);
readback.Unmap();
```

### 所有と同期

queueはdeviceが所有し、command bufferとsubmissionはdeviceの子resourceとして数える。生存する子があるdeviceのDisposeは拒否する。encoderはcommand bufferに属する非所有のpass handleで、Endしてからcommand bufferをFinish／破棄する。command bufferはsubmit後もsubmission完了までnative allocator／command memoryを保持する。

resourceへのCPU参照を記録に保持しても、利用側がDisposeを呼ぶ権利とGPU寿命の責任は移らない。記録からsubmit、GPU完了まで参照先resourceを生存させ、unmappedを要求するGPUアクセス中にmapしない。submit時にも記録したresourceの生存を検証する。CPUアクセス、CPU書き込み、GPU使用、resource解放の同期は利用者が管理する。

内部lock、atomic counter、暗黙のGPU完了待機、複数commandの並列呼び出しに関する保証を設けない。native APIが必要とするCPU cache flush／invalidateは既存のMap／CopyFrom／CopyTo／Unmapの契約で扱い、GPU barrierやsubmitと混同しない。

### Backend文書の責務

native command encoder／allocator、queue、completion fence／callback、barrier変換、copy layout、resource保持とnative APIの制約は各backend READMEへ記載する。backendのnative handleを利用側APIへ出さず、nativeの進行処理をWaitAsync／Statusへ対応させる。どのAPIでも記録・submit・完了の境界を維持する。

## 検討した代替案

- resource自身にGPUコピーやbarrierを持たせる: resourceのCPUアクセスとGPU実行を混ぜるため採用しない。
- upload／readbackのstaging・copy・submit・waitを自動化する: 利用者が操作の単位と同期を選べなくなるため採用しない。
- reusable command bufferと複数queueを最初から共通化する: reset、再submit中のresource lifetimeとqueue ownershipの契約を先に広げるため初期範囲から外す。
- Disposeでsubmit／wait／pass終了を自動化する: 例外時にGPU実行や同期を起こすため採用しない。未submitの記録の破棄だけを許可する。

## 結果と影響

CPUコピー、GPU記録、submitと待機の境界が明確になる。利用者はstaging、padding、barrier、resource lifetimeを明示的に管理する。one-shotと単一queueに限定することで、copy・clear・passの実行経路を共通APIで設計できる。

初期render passは既存color textureを使用する。depth/stencilとMSAAは対応するresourceの拡張で扱う。

## 検証方針

shared sample／testの本体は共通APIのみを使用し、backend固有deviceの作成はbootstrapに置く。明示的なUpload → copy → Readback → wait → map → CPU copyの全byte一致、textureのrow／layer paddingとpartial region、clearとstore、pass順序、完了待機を既存CIで検証する。

alignment違反、size／usage／device不一致、mapped buffer、pass中のcopy／barrier、Finish忘れ、二重submit、submit後の元list変更、キャンセル後の再wait、Pending中のDisposeと失敗時の状態を確認する。CPUコピーがnative GPU copy／submitを呼ばないこと、記録がsubmit／waitを起こさないことを検証する。
