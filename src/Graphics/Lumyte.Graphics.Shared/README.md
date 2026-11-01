# Lumyte.Graphics.Shared

グラフィックスバックエンドから `ProjectReference` で参照する共通実装ライブラリです。ソースのリンクコンパイルは使用しません。

- `ShaderDataLayout` は artifact の型 schema を読み、生成時の互換性確認と値の配置を行います。

  `Data(artifact, target, name)`はtargetの展開・reflection解析・layout構築をartifactの寿命内で共有します。`Data(target, name)`・`Root`・`HasRoot`も不変のtargetごとに解析結果を再利用します。所有者は弱参照で結び付け、GPU資源を保持しません。別artifact・別target・同名の異なるschemaを分離し、公開されるshader codeのコピー契約を維持します。成功したlayoutだけを保存し、存在しない名前の問い合わせでキャッシュを増やしません。
- `ShaderBindingSnapshot` は選択した要素から参照を収集し、循環と転送済み要素の依存snapshotを扱います。
- `ShaderDataTransferState` は明示copyのmetadata overlayを管理し、submit成功後に公開します。
- `ShaderBindingData` と `WgslBindingSpecializer` は WGSL を使うバックエンドで必要な binding データと shader variant を生成します。Vulkan は snapshot と layout を利用し、参照の GPU 表現は自身で生成します。
- `ShaderBufferBinding` はallocationと登録byte範囲を保持します。同じallocationでも異なる範囲は別bindingとして解決し、GetElementはそのbinding内の要素位置を指定します。
- `GraphicsDeviceLifetime` は各backendが観測したdevice lossを一度だけ記録し、保存済みの状態、通知、loss待機を共通管理します。

バックエンドが実装する `IShaderReference`、`IShaderDataSource`、`IShaderRawBuffer`、`IShaderDataLayout` は `Lumyte.Graphics.Abstractions` に定義します。バックエンド向けの公開 API を経由するため `InternalsVisibleTo` は必要ありません。利用側のアプリケーションは従来どおり共通 graphics API を使用します。

GPU同期とリソースの有効期間は利用者が管理します。このライブラリはGPU同期や転送命令の自動発行を追加しません。metadataキャッシュの初期構築と公開は内部lockで保護します。

`ShaderBindingSnapshot`の収集と`ShaderDataTransferState`のmetadata overlayはbinding生成に必要な情報だけを保持します。登録の失効、staging revision、command間依存の変化をSubmit時に再検証しません。

`SurfaceFrameLifetime`は取得・提示の局所状態を扱い、ネイティブ取得・提示の明示的な完了待機を提供します。GPU submissionを自動収集せず、GPU使用の完了は利用者が別途確認します。semaphoreのsignal／wait履歴、使用中resourceの保持数、検証callbackは管理しません。

共通の責務は[GRAPHICS-0014](../../../docs/adr/graphics/GRAPHICS-0014-caller-managed-resource-validation.md)に従います。
