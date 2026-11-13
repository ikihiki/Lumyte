# ADR-GRAPHICS-0023: 単位を明示するGraphics APIと描画初期化

- 状態: 採用
- 日付: 2026-10-26

## 背景

byteと要素の範囲指定が同じ名前を使い、shader ABI bufferとraw bufferも同じ生成名を使っていた。またArgument Tableのsetterが後続描画へ選択状態を渡しておらず、引数が指定tableの外を参照していないか検証できていなかった。

## 決定

単位と生成物を明示する名前を正式なinterfaceメンバーにする。旧名と転送専用の拡張メソッドは削除し、実装と利用側を移行する。

比較元はmainの`16d856a`。対象メンバーの変更は以下のとおり。

```diff
 namespace Lumyte.Graphics.Abstractions
 {
     public interface IGraphicsBuffer<T> where T : unmanaged
     {
-        BufferSlice<T> Slice(ulong offset, ulong count);
+        BufferSlice<T> SliceElements(ulong offset, ulong count);
-        void ValidateRange(ulong offset, ulong length);
+        void ValidateByteRange(ulong offset, ulong length);
-        void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);
+        void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);
-        void CopyTo(Span<byte> destination, ulong offset, ulong length);
+        void CopyBytesTo(Span<byte> destination, ulong offset, ulong length);
     }
     public interface IGraphicsShaderDataBuffer<T> where T : struct, IShaderData
     {
-        ValueTask MapAsync();
+        ValueTask MapAsync(CancellationToken cancellationToken = default);
     }
     public interface IGraphicDevice
     {
-        IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic) where T : struct, IShaderData;
+        IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic) where T : struct, IShaderData;
     }
     public interface IRenderEncoder
     {
         // このtableに属する参照だけをdraw／dispatchで受け付ける。
         void SetArgumentTable(IArgumentTable table);
     }
     public interface IComputeEncoder
     {
         // このtableに属する参照だけをdraw／dispatchで受け付ける。
         void SetArgumentTable(IArgumentTable table);
     }
+    public static class RenderEncoderExtensions
+    {
+        public static void SetDynamicState(this IRenderEncoder encoder, Viewport viewport, ScissorRect scissor, BlendConstant blendConstant = default, uint stencilReference = 0);
+    }
     public sealed record GraphicsRenderStateDesc
     {
+        public static GraphicsRenderStateDesc ForColorTargets(int colorAttachmentCount);
     }
 }
```

両bufferの要素範囲はSliceElementsに統一する。shader bufferのMapAsyncはraw storageのキャンセル契約を使用し、native完了後に取消を観測した場合はmappingを解放してから取消を返す。CopyFrom／CopyToの型付き要素操作はbyte操作と異なるため維持する。

SetDynamicStateはviewport、scissor、blend constant、stencil referenceを順に明示設定する。後続setterの失敗は先に設定した値を巻き戻さない。ForColorTargetsは指定個数の既定color stateを作り、負数は拒否する。暗黙のpass開始・copy・barrier・submit・GPU待機は追加しない。

### Argument Table

SetArgumentTableを正式な状態設定として維持し、[GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md)のtable選択契約を実装する。指定tableは同じdeviceに属する生存中のtableに限り、別deviceや別backendのtableはArgumentException、破棄済みtableはObjectDisposedExceptionで拒否する。選択はpassごとに独立し、SetArgumentsの前後どちらでも変更できる。

draw／dispatch（indirectを含む）のbinding収集時に、root引数と到達するshader data内のすべてのIGpuRefが、現在選択したtableに属することをReferenceEqualsで検証する。参照があるのにtable未設定ならInvalidOperationException、別tableの参照ならArgumentExceptionで拒否し、native描画・dispatchを記録しない。slot番号や実体resourceが同じでも、別tableの登録は受け付けない。command内の明示的copyで転送したmetadataにも同じ検証を適用し、循環は既存の到達集合で停止させる。

所属検証はbinding生成に必要な既存の参照収集と同時に行い、検証専用の再走査は増やさない。この所属検証を除き、[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)の寿命・登録失効・GPU同期を利用者が管理する契約は変えない。値だけの引数と引数なしpipelineではtableを要求しない。

root引数のないpipelineへ切り替えた場合は以前のsnapshotを使用しない。root引数のある別pipelineへはSetArgumentsを再度必要とする。

## 検討した代替案

- 旧interfaceと別名拡張を両方残す案は、同じ操作の入口が増えるため採用しない。
- SetArgumentTableを削除して参照自身からtableを推論する案は、指定table外の参照を拒否する境界がなくなるため採用しない。
- 暗黙の描画初期化は設定箇所が見えなくなるため、明示的なhelperとする。
- bufferの共通基底interfaceはraw layoutとshader ABIやreadback対応の違いを隠すため作らない。

## 結果と影響

外部backendと利用側は新しいinterfaceへ移行する必要がある。単位・shader ABI・キャンセルの契約はAPI名と引数に表れる。範囲、mappingの取消と解放、描画初期化、backendのpipeline切り替えと未対応設定の拒否を検証する。table未設定・別table・間接参照・table切り替え・値だけの引数についても検証し、サンプルと描画ライブラリは使用するtableを明示する。
