# Lumyte.Graphics.Shared

グラフィックスバックエンドから `ProjectReference` で参照する共通実装ライブラリです。ソースのリンクコンパイルは使用しません。

- `ShaderDataLayout` は artifact の型 schema を読み、値の検証と配置を行います。
- `ShaderBindingSnapshot` は選択した要素から参照を収集し、循環と転送済み要素の依存snapshotを扱います。
- `ShaderDataTransferState` は明示copyのmetadata overlayを管理し、submit成功後に公開します。
- `ShaderBindingData` と `WgslBindingSpecializer` は WGSL を使うバックエンドで必要な binding データと shader variant を生成します。Vulkan は snapshot と layout を利用し、参照の GPU 表現は自身で生成します。
- `ShaderBufferBinding` はallocationと登録byte範囲を保持します。同じallocationでも異なる範囲は別bindingとして解決し、GetElementはそのbinding内の要素位置を指定します。

バックエンドが実装する `IShaderReference`、`IShaderDataSource`、`IShaderRawBuffer`、`IShaderDataLayout` は `Lumyte.Graphics.Abstractions` に定義します。バックエンド向けの公開 API を経由するため `InternalsVisibleTo` は必要ありません。利用側のアプリケーションは従来どおり共通 graphics API を使用します。

同期とリソースの有効期間は利用者が管理します。このライブラリは内部同期や転送命令の自動発行を追加しません。
