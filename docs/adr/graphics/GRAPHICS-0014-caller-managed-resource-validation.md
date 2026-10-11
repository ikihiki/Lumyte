# ADR-GRAPHICS-0014: 利用者が管理するリソース寿命と実行時検証

- 状態: 採用
- 日付: 2026-10-10

## 背景

GraphicsはCPU／GPU同期とリソースの有効期間を利用者が管理する低レイヤAPIである。
寿命の誤用を検出するために子resource数、登録状態、commandごとの検証callback、texture subresourceの状態表を維持すると、正常な操作にも更新、確保、走査の費用が発生する。
特にdraw／dispatchのshader snapshotを再走査し、Submit時にも参照先を検証する処理は、binding生成に必要な仕事を終えた後にも追加の費用を生じさせる。

利用者が守る事前条件と、ライブラリが毎回その違反を検出する保証を分け、検証だけを目的とした追跡をGraphics API全体から除く。

## 決定

### 利用者が保証する条件

利用者はresource、view、shader、pipeline、argument table、command、submission、surface、swapchain、frame、semaphoreの生存期間と解放順を管理する。
GPUと提示処理が使用中のobjectを破棄せず、deviceを最後に解放する。
登録を置換・Releaseした後やtableを解放した後に、古いIGpuRefとその派生参照を使わない。
commandに記録したresourceや登録を、必要な処理が完了するまで有効に保つ。

barrierのBefore／After状態、subresourceの実際の状態、CPU mappingとGPU accessの排他、semaphoreのsignal／waitと再利用は利用者が保証する。
画像取得・GPU実行・提示の依存と完了待機も利用者が明示する。
共通APIはこの管理を代行せず、違反時に必ずmanaged例外で検出できるという保証を提供しない。

### 取り除く処理

- deviceのlive child数、textureのlive view数、resourceのlive registration数、shaderのprogram保持数を用いた解放拒否。
- 登録の世代・失効検出のための照合と、draw／dispatch／Submit時のresource生存・mapping・登録有効性の再走査。
- command内に検証callbackを蓄積する処理と、Submit時のcallback呼び出し。
- shader dataのstaging revisionや、別command間のmetadata依存が変化していないことを確認する履歴・集合。
- textureのmip／layerごとにBeforeStateを照合する状態表と、使用箇所から状態表を参照する処理。
- commandから取得Frameを自動収集し、FrameごとのSubmit回数・最終Present状態・GPU完了を追跡する処理。
- semaphoreのsignal発行・wait消費の履歴、使用中判定、重複検出集合と、それらによる解放拒否。
- Disposeの可否判定だけを目的としたGPU完了照会、および生成・記録済み値の繰り返しのschema走査。

任意のvalidation modeへこれらを移すAPIは追加しない。native実装が返すエラーは引き続き伝達する。

### 維持する処理

CPU memoryへ触れるspanの範囲検査、checkedなサイズ計算、現在操作するinstance自身のdisposed／mapping状態、null・enum・必須値などの局所的な引数検査は維持できる。
native descriptorへの変換、ネイティブ失敗結果の処理、生成途中の失敗時cleanupも引き続き行う。
shader artifactの読み込みやpipeline作成時のschema／ABI互換性確認は、確定した情報として再利用する。

binding生成に必要なIGpuRefの収集、要素範囲、循環を止める訪問済み集合、resourceの重複排除、型layoutに従ったpackingは機能として必要であり、保持する。
有限なbindingを生成するための容量算出と、生成結果が対応上限に収まるかの確認も行う。
この収集はresourceの生存を保証する所有権や、Submit時に再検証するための検証リストを作らない。

shader dataの明示copyはCPU依存metadataも同じ要素範囲へ伝播する。
command内のcopy後の参照解決にはmetadata overlayを使い、Submit成功後に後続commandの記録へ公開する。
copy記録時のmetadataとGPUが実際に読むstaging bytesの一致を利用者が保証し、copy完了前のstaging再利用を避ける。
別commandの転送結果に依存するdrawは、必要なmetadataが公開された後に記録する。
必要なmetadataの伝播は行うが、履歴の再照合によって呼び出し順の誤りを検出しない。

backendが生成したnative command、binding、root backing、pipeline variantなどを適切に所有・解放する処理は維持する。
利用者のresourceに対する検証専用の保持カウンターとは区別し、GPUが使用する内部objectを解放する時点も利用者がcommand等の寿命として管理する。

### 公開APIと契約の差分

比較元: main `746eab3`。既存APIの形は維持し、以下のコメントで示す事前条件と検証保証を変更する。
Surface・Swapchain・Presentは未取り込みの[GRAPHICS-0013](GRAPHICS-0013-surface-swapchain-presentation.md)へ直接反映する。

```diff
 namespace Lumyte.Graphics.Abstractions;
 
 public interface IGraphicsTexture : IDisposable
 {
+    // 利用者がViewを先に解放し、TextureのGPU使用完了を保証する。
+    // live view数による親TextureのDispose拒否は保証しない。
     IGraphicsTextureView CreateView(TextureViewDesc desc);
 }
 
 public interface IArgumentTable : IDisposable
 {
+    // 登録と参照先の寿命を利用者が保証する。
+    // 置換・Release・table解放後の参照を使用しない。失効検出は保証しない。
     IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view);
     void ReleaseTexture(uint slot);
 }
 
 public interface IGraphicsQueue
 {
+    // 必要なresource・登録・staging bytesを利用者が有効に保つ。
+    // Submit時の依存resourceの生存・mapping・revision再検証は行わない。
     IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers);
 }
 
 public interface IShaderReference
 {
-    void Validate();
 }
 
 public interface IShaderDataLayout
 {
-    void Validate(ShaderValueSnapshot snapshot);
 }
```

`Dispose`の冪等性、明示的な`Status`／`WaitAsync`による完了照会、commandの記録・確定に必要な局所状態は維持する。
`Dispose`はGPU完了を調べて解放可否を決めず、利用者が完了条件を満たして呼び出す。
通常の引数不正や未対応形式などで返される例外と、誤った寿命・同期に対する検出保証を混同しない。

### 置換する判断

以下のADRのうち、列挙した検証保証だけを置き換える。型、Desc、転送、shader ABI、binding生成などの他の決定は維持する。

- [GRAPHICS-0002](GRAPHICS-0002-typed-buffers.md): bufferが残るdeviceの解放拒否。
- [GRAPHICS-0003](GRAPHICS-0003-textures-and-views.md): live view／child数による親resourceの解放拒否。
- [GRAPHICS-0004](GRAPHICS-0004-samplers.md)と[GRAPHICS-0006](GRAPHICS-0006-shader-compilation-and-modules.md): deviceの子resource数による解放拒否。
- [GRAPHICS-0005](GRAPHICS-0005-argument-tables-and-gpu-references.md): 登録の世代・失効検出と、live registration数によるresourceの解放拒否。
- [GRAPHICS-0007](GRAPHICS-0007-command-buffers-and-submission.md): 記録resourceの再検証、subresource状態表、使用中の解放拒否。
- [GRAPHICS-0008](GRAPHICS-0008-pipeline-programs-and-render-state.md): shaderの保持数による解放拒否と、記録済みprogramの生存再検証。
- [GRAPHICS-0009](GRAPHICS-0009-shader-argument-binding.md): draw／Submitでの登録失効検出、staging revisionとcommand間metadata依存の再検証。
- [GRAPHICS-0011](GRAPHICS-0011-indexed-and-indirect-commands.md): index／indirect bufferのSubmit時の寿命とmapping再検証。

## 検討した代替案

検証用追跡を常時維持する方式は、利用者が管理する情報を二重に更新し、正常なdrawとSubmitにも費用を課すため採用しない。
検証を無効にしてもカウンターやcallback生成だけを残す方式は、確保と更新費用が残るため採用しない。
すべての入力検査とmetadataを削除する方式は、CPU memory accessとbackendが必要とするbinding生成を成立させないため採用しない。

## 結果と影響

記録・描画・提出時の検証専用の確保、集合操作、resource走査、親子カウンター更新を省く。
CPU／GPU同期と寿命に違反した呼び出しは、managed例外で失敗する保証がなくなり、native errorや不正なGPU実行につながり得る。
利用側は正しい呼び出し順と明示的な完了待機を設計する必要がある。

共通APIのテストは有効な寿命と同期の下で描画・転送・参照解決を確認する。
削除した検証のために意図的に使用中resourceを破棄したり、失効参照をnativeへ渡したりするテストは実行しない。
CPU範囲、通常の引数、schemaの生成時互換性、nativeエラー処理のテストは維持する。
実行テストは既存CIへ任せ、性能改善の数値は測定を行うまで主張しない。
