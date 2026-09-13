# Native feature RenderGraph

`NativeRenderProvider` は共通の機能 graph を DirectX 12／Vulkan 用の内部 pass に展開します。
外部 assembly は `NativeRenderPassRegistry` と `INativeRenderPass<TRequest,TResult>` を実装し、
公開された build／record context だけで機能を追加できます。production の `InternalsVisibleTo` は使いません。

内部用途から texture usage を確定し、Native Resources の scope、view、descriptor、batch を準備してから
同期 callback を呼びます。DirectX 12 は texture の access と layout を texture transition で処理し、
global barrier は buffer に適用します。Vulkan は General layout と global dependency を使います。
Copy は Native API の texture-to-memory／memory-to-texture 命令と GPU 内の scratch range を使います。

Submit は最初の await より前に import の使用保持を取得します。GPU の batch 保持と export の pin は別々で、
execution を返却しても未完了 GPU の参照は解放しません。`StopAccepting()` は新しい提出・取得を閉じ、
既に受け付けた build／upload は終了時に drain します。scope、pin と execution は caller が返却します。

共通 package upload は `images.sampled` version 1 の準備済み Linear／Premultiplied または Opaque な
RGBA8／BGRA8 を扱います。CPU の row／slice stride が 0 の場合は tightly packed とし、Native の転送 pitch へ
明示的に並べ替えます。file／URI の取得、復号、色変換は行いません。

段階 0 の実装です。内容世代 ticket、内部 alias 配置、描画 cache、mesh による機能最適化は後続段階です。
Native の公開 interoperability (`NativeResources.Manager` など) を直接使う作者は、下位 manager と同じ
操作の直列化・明示的な所有契約に従います。
