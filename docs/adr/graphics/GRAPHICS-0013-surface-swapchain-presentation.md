# ADR-GRAPHICS-0013: Surface・Swapchain・Presentの明示的な管理

- 状態: 採用
- 日付: 2026-10-10

## 背景

描画結果を表示先へ提示するには、外部の表示先との接続、表示画像の取得、GPU実行と提示の同期が必要になる。
ウインドウ作成やハンドル取得はGraphicsの責務に含めず、別PRで実装する。

## 決定

### 責務と操作

表示先を借用するSurface、設定と画像群を管理するSwapchain、取得した画像を借用するFrameを分ける。
インターフェースとDescはGraphics.Abstractionsに置き、バックエンドが実装する。
Surfaceの生成は各バックエンド固有の入口で既存のハンドルを受け取る。共通デバイスにウインドウ型や生成の抽象化を追加しない。
ハンドルの型、ネイティブ拡張、adapter選択、画像取得と提示の実装詳細は各バックエンドのREADMEに記載する。

利用側は対応形式とmodeを照会し、正確なpixel sizeでSwapchainを生成する。
フレームを取得してTextureからViewを作り、明示的なbarrierとcommandを記録する。
取得画像の最後の状態はPresentとし、Queue.Submit(commands, frame)へ渡す。
このsubmitはバックエンドに必要な取得・提示のネイティブ同期を関連付けるが、描画や転送、barrier、Presentを自動発行しない。
Frame.PresentはGPUのCPU完了待機を要求せず提示を要求する。
Frame.WaitForReleaseAsyncは画像leaseの解放条件を明示的に待つ操作であり、実際の表示時刻を保証しない。

### サイズと復旧

configurationのWidthとHeightはpixel単位で、暗黙のclampや形式・modeのfallbackを行わない。
ゼロサイズの表示先は利用側が取得を停止する。ネイティブに現在サイズが固定される表示先は、そのサイズをcapsで返す。
Timeoutではフレームを返さず、利用側が再試行する。Suboptimalでは有効なフレームを返す。
Outdated、Lost、DeviceLostではフレームを返さず、利用側がSwapchainの再構成、Surfaceの再生成、Deviceの再生成を選択する。
取消はネイティブ取得前に確認する。取得に成功した場合は必ずFrameを返し、取得後の取消へ変換して画像を失わない。

### 所有権と同期

Surfaceは外部ウインドウ・canvasを所有しない。外部targetはSurfaceの解放まで有効に保つ。
Surfaceにつき一つのactive Swapchainを持つ。再構成・解放はすべてのframeを解放してから行う。
バックエンドが許容する範囲で複数frameを取得でき、共通APIは全バックエンドへ単一frame制約を課さない。
同期と呼び出しの排他は利用側が管理し、ライブラリはlockや自動のGPU idle待機を追加しない。

FrameのTextureは借用品であり、利用側から直接Disposeできない。
記録・submitはAcquiredの間だけ許可し、frameごとに一度のsubmitへ必要なcommandをまとめる。
Frameを使うsubmitでは取得画像を使ったcommandと最終Present状態を検証する。
通常のsubmitへ取得画像を混入させる、別のframeを混入させる、失効した画像・Viewを再利用する操作は拒否する。
PresentはSubmittedから一度だけ行い、二重Presentを拒否する。
Frame.Disposeは未submitの画像を提示せず返却でき、submit済みではGPUとネイティブ提示の解放条件が満たされるまで拒否する。
先にViewとcommandを解放する。Frame、Swapchain、Surface、Deviceの順に所有権を解放する。

### 公開API差分

比較元: main `746eab3`。コメントは契約を示す。

```diff
+namespace Lumyte.Graphics.Abstractions;
+
+// Owns the graphics connection to a borrowed presentation target.
+// The caller keeps the native target alive and manages all access synchronization.
+public interface IGraphicsSurface : IDisposable
+{
+    // Queries current device-specific target capabilities without configuring it.
+    SurfaceCapabilities GetCapabilities();
+
+    // Creates the surface's sole active swapchain.
+    IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc);
+}
+
+// Contains a snapshot of this surface's capabilities on its selected device.
+public sealed record SurfaceCapabilities
+{
+    // Gets the supported color formats, in backend preference order.
+    public required IReadOnlyList<TextureFormat> Formats { get; init; }
+
+    // Gets the supported scheduling policies.
+    public required IReadOnlyList<PresentMode> PresentModes { get; init; }
+
+    // Gets the supported alpha composition modes.
+    public required IReadOnlyList<SurfaceAlphaMode> AlphaModes { get; init; }
+
+    // Gets the permitted image usages.
+    public required TextureUsage SupportedUsage { get; init; }
+
+    // Gets the minimum pixel width.
+    public uint MinWidth { get; init; } = 1;
+
+    // Gets the minimum pixel height.
+    public uint MinHeight { get; init; } = 1;
+
+    // Gets the maximum pixel width.
+    public required uint MaxWidth { get; init; }
+
+    // Gets the maximum pixel height.
+    public required uint MaxHeight { get; init; }
+
+    // Gets the required current width, or null when the caller chooses within the limits.
+    public uint? CurrentWidth { get; init; }
+
+    // Gets the required current height, or null when the caller chooses within the limits.
+    public uint? CurrentHeight { get; init; }
+}
+
+// Requests an exact pixel size, format, usages and presentation policy.
+public sealed record SwapchainDesc
+{
+    // Gets the positive pixel width; zero-size targets are suspended by the caller.
+    public required uint Width { get; init; }
+
+    // Gets the positive pixel height.
+    public required uint Height { get; init; }
+
+    // Gets the requested surface-supported color format.
+    public TextureFormat Format { get; init; } = TextureFormat.Bgra8Unorm;
+
+    // Gets the exact usages, including RenderAttachment.
+    public TextureUsage Usage { get; init; } = TextureUsage.RenderAttachment;
+
+    // Gets the requested scheduling mode without silent fallback.
+    public PresentMode PresentMode { get; init; } = PresentMode.Fifo;
+
+    // Gets the requested alpha composition mode.
+    public SurfaceAlphaMode AlphaMode { get; init; } = SurfaceAlphaMode.Auto;
+}
+
+// Owns a configured presentation image set; the caller explicitly reconfigures it.
+public interface IGraphicsSwapchain : IDisposable
+{
+    // Gets the requested and effective configuration without implicit size correction.
+    SwapchainDesc Configuration { get; }
+
+    // Reconfigures only after every acquired frame has been disposed.
+    void Reconfigure(SwapchainDesc desc);
+
+    // Attempts acquisition without waiting for an unavailable image.
+    ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(CancellationToken cancellationToken = default);
+}
+
+// Returns a leased image only for Success or Suboptimal.
+public readonly record struct SurfaceAcquireResult(SurfaceStatus Status, IGraphicsSurfaceFrame? Frame);
+
+// Owns one presentation image lease and its native synchronization objects.
+public interface IGraphicsSurfaceFrame : IDisposable
+{
+    // Gets the acquisition, submission and presentation lifetime state.
+    SurfaceFrameStatus Status { get; }
+
+    // Gets the borrowed image; do not dispose it directly.
+    IGraphicsTexture Texture { get; }
+
+    // Requests presentation after the frame's explicit queue submission without a CPU completion wait.
+    SurfaceStatus Present();
+
+    // Explicitly waits until GPU and presentation use allow frame disposal.
+    ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default);
+}
+
+// Specifies the lifetime of one acquired presentation image.
+public enum SurfaceFrameStatus
+{
+    // The texture may be recorded and submitted once.
+    Acquired,
+
+    // The explicit queue submission owns the image's GPU use.
+    Submitted,
+
+    // Presentation was requested; the image cannot be reused.
+    Presented,
+
+    // The frame and its image lease were released.
+    Disposed,
+}
+
+// Reports image acquisition or presentation without automatic recovery.
+public enum SurfaceStatus
+{
+    // The operation succeeded.
+    Success,
+
+    // The image is usable, but reconfiguration is recommended.
+    Suboptimal,
+
+    // No image was immediately available; retry later.
+    Timeout,
+
+    // The swapchain must be explicitly reconfigured.
+    Outdated,
+
+    // The target was lost; recreate its surface.
+    Lost,
+
+    // The graphics device was lost; recreate the device.
+    DeviceLost,
+}
+
+// Specifies the requested presentation scheduling policy.
+public enum PresentMode
+{
+    // Queues images for display in order at vertical synchronization.
+    Fifo,
+
+    // Keeps the newest queued image for the next vertical synchronization.
+    Mailbox,
+
+    // Allows presentation without waiting for vertical synchronization.
+    Immediate,
+}
+
+// Specifies how presentation alpha is composed.
+public enum SurfaceAlphaMode
+{
+    // Selects a backend-supported composition mode.
+    Auto,
+
+    // Treats the presented image as opaque.
+    Opaque,
+
+    // Uses colors already multiplied by their alpha.
+    Premultiplied,
+}
+
 public interface IGraphicsQueue
 {
+    // acquired frameを一度だけsubmit。commandは取得画像を使い、最後にPresentへ遷移する。
+    IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, IGraphicsSurfaceFrame frame);
 }
 public enum TextureState
 {
+    // 取得画像だけに許可する。Before/After scopeのAccessはNoneで提示へ解放する。
+    Present,
 }
```

Present stateのbarrierはネイティブ同期に必要な状態遷移を記録し、Present自体は実行しない。
通常のTextureにPresent stateを指定するとArgumentExceptionになる。
設定の値が不正ならArgumentException、対応外の形式・usage・modeならNotSupportedExceptionを返す。
不正な所有権や状態遷移はInvalidOperationException、解放済みのリソースはObjectDisposedExceptionで拒否する。

## 検討した代替案

取得、submit、present、resizeを一つのrender loopへ自動化すると利用側のcommand発行と同期方針を制約するため採用しない。
Frameを通常の所有Textureとして返すとネイティブSwapchain画像を誤って解放できるためleaseにする。
共通のWindowHandle型を定義するとWindowingとの結合が増えるため、生成はバックエンド固有にする。
Present前に必ずCPUでsubmission完了を待つ方式はGPUの提示同期が可能な実装で直列化を強制するため採用しない。

## 結果と影響

既存のRenderPass、TextureView、command APIを取得画像にも使える。
表示先の生存、リサイズ、取得失敗への復旧、frameの解放を利用側が明示的に管理する必要がある。
画像のnative ownershipと同期の差はバックエンドへ閉じ込める。
共通APIのテストはleaseの失効、再構成、二重提示、明示的なsubmit、コピー読み戻しを確認する。
実ウインドウのハンドル取得とそのintegration testは後続PRに分離する。
