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

## 保持する転送表現

`ShaderDataLayout.CreateStorage<T>` は生成された静的schemaとartifactを、値に依存せず照合します。保持領域はartifactのoffsetへ数値と16 byteの不透明参照値を配置し、typed reference identityは別のCPU配列へ保持します。`Upload` は同device・同schemaを確認し、指定範囲の全snapshotを準備した後にmapped bufferへbytesをコピーします。通常の引数不正や準備の失敗では、destinationのbytesとmetadataを変更しません。

各凍結要素は一要素のbytesと参照配列を所有します。数値のReadはそのbytesから遅延して論理ShaderValueを返しますが、mutableなsource、元のstorageや巨大な一括copy全体を保持しません。型とlayoutの不変情報だけを共有し、新しいprocess-wide cacheは追加しません。

このfactoryとUploadはbackend向けの公開接続であり、backendが公開しない製造identity、参照の製造元検査とencoding、destinationのmappingと範囲検査を提供します。製造identityにはapplicationが知るdevice objectを使わず、公開factoryで任意のencoderから作った値をbackendが受け入れないようにします。公開IShaderReferenceの実装だけを製造元の証拠とせず、各backend内部のmarkerを使います。既存のtransfer overlayとSubmit成功後の公開処理を変更せず、登録の失効追跡やSubmit時の再検証は追加しません。
