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
取得画像の最後の状態はPresentとし、Queue.Submitへcommandを渡す。FrameはSubmitの引数にしない。
利用側がbinary semaphoreを生成し、画像取得のsignal、Submitのwait／signal、Presentのwaitをそれぞれ指定する。
バックエンドは渡された同期だけを実行し、取得waitや描画完了signal、Present waitを補完しない。
Frame.PresentはGPUのCPU完了待機を要求せず提示を要求する。
Frame.WaitForReleaseAsyncはネイティブの取得・提示処理の完了を明示的に待つ操作であり、GPUのcommand完了や実際の表示時刻を保証しない。GPU使用の完了は利用側がsubmissionから別途確認する。

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
利用側は取得から提示までに必要なcommandを記録・提出し、画像をPresent状態へ遷移させる。
SubmitはcommandからFrameを収集せず、画像の寿命、提出回数、最終状態を追跡しない。一回のSubmitで複数Frameを使用できる。
失効した画像・Viewを再利用しないこと、すべてのGPU使用と提示の依存を満たすことは利用側が保証する。
PresentはAcquiredから一度だけ行い、局所的な二重Presentの検査は維持する。
Frame.Disposeは利用側がGPU使用とネイティブ取得・提示の完了を確認してから呼ぶ。未提示画像は提示せず返却する。
子resource数による解放拒否やDispose時のGPU完了照会は行わない。
先にViewとcommandを解放する。Frame、Swapchain、Surface、Deviceの順に所有権を解放する。

### 複数ウインドウと表示先

同一Deviceは複数のSurfaceを所有でき、各Surfaceが一つのSwapchainを持つ。
Device生成とSurface生成は分離する。Device生成には表示先やSurface生成callbackを渡さない。
生成済みDeviceへtargetを渡すバックエンド固有の入口を設ける。利用者は各Surfaceのcapabilitiesを照会し、Deviceとqueueで使用可能な設定を選ぶ。
途中でnative生成などに失敗した場合、今回生成したSurfaceとnative資源を解放し、部分的な所有結果を返さない。
生成後も、互換性のある表示先を同じDeviceへ追加できる。別Deviceが必要な表示先を暗黙に移し替えない。

取得画像、サイズ、mode、取得失敗、再構成、解放条件は各Swapchainへ閉じる。
一つのウインドウの未提出Frameや生存中のleaseが、別ウインドウの取得・提出・再構成・解放を禁止しない。
ウインドウごとの画像を独立したcommand／Submitで描画でき、一つのcommandまたはSubmitへ複数ウインドウの描画をまとめることもできる。
一括Submitでも同期は利用側が明示し、各取得のsemaphoreをwaitへ、各Presentのsemaphoreを別々のsignalへ指定する。
binary semaphoreの一回のsignalを複数ウインドウのPresentで共有しない。Presentするウインドウごとに一回のwaitを用意する。
PresentはFrameごとの操作であり、複数ウインドウの同時表示時刻やnative処理の非ブロッキング性は保証しない。

一つのSurface／Swapchainのリサイズ・復旧・解放は、他のSurfaceの設定やleaseを変更しない。
各ウインドウの更新頻度と停止・再開は利用側が選び、ライブラリは一括idle待機や共通のフレーム境界を設けない。
Deviceの解放だけは、接続したすべてのSurfaceと子resourceを解放してから行う。
ウインドウ生成・ハンドル取得は引き続き別PRとし、複数targetの受け取り口と共通APIでの描画を扱う。

### 明示的なbinary semaphore

IGraphicDevice.CreateSemaphoreは未signalの所有semaphoreを生成する。
画像取得には任意のsignal semaphoreを渡し、Success／Suboptimalの場合だけsignalが発行されたものとして扱う。
nullならsemaphoreのsignalを発行せず、利用側が画像の取得完了を明示的に待ってから使用する。
SubmitのwaitにはsemaphoreとGPUの待機stageを渡し、signalはそのSubmitのcommand実行完了後に行う。
Presentには待機semaphoreのリストを渡す。空リストも許可するが、画像取得・描画完了・提示の依存を満たす責任は利用側にある。

一回のsignalは一回のwaitで消費する。signal済みへの再signal、signal未発行へのwait、重複、同じSubmitでの同一semaphoreのwaitとsignalを避け、同じDeviceのsemaphoreだけを指定する責任は利用側にある。ライブラリはsignal／wait履歴や重複集合を維持しない。
GPUへ発行済みのsignalはCPU完了前からwaitに指定できる。未発行のsignalを将来待つ方式は今回の単一queue契約には含めない。
waitが発行されると次のsignalを指定できるが、nativeで許される再利用時点と呼び出しの同期は利用側が管理する。
semaphoreは発行済みのGPU／提示／取得処理が完了してから利用側がDisposeする。解放時の完了照会やCPU待機は行わない。
失敗した画像取得やネイティブ発行前の失敗については、利用側が結果に従って同期の発行有無を扱う。
commandが空でもwait／signalがあればSubmitを許可し、取得を破棄する場合のsignal消費などにも使用できる。
同期は実行順序を表すもので、明示的なbarrierやTextureState.Presentへの遷移を代替しない。

```csharp
using IGraphicsSemaphore acquired = device.CreateSemaphore();
using IGraphicsSemaphore rendered = device.CreateSemaphore();
SurfaceAcquireResult result = await swapchain.AcquireNextFrameAsync(acquired);
IGraphicsSurfaceFrame frame = result.Frame!;
// Frame.Textureを使って記録し、最後にPresentへ遷移する。
using IGraphicsSubmission submission = device.Queue.Submit(new QueueSubmitDesc
{
    CommandBuffers = [commands],
    WaitSemaphores = [new() { Semaphore = acquired, Stages = PipelineStage.AllCommands }],
    SignalSemaphores = [rendered],
});
frame.Present([rendered]);
await submission.WaitAsync();
await frame.WaitForReleaseAsync();
// Viewとcommandを先に解放してからFrameとsemaphoreを解放する。
```

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
+    // signalSemaphoreは成功時だけsignal。nullではsemaphoreを自動生成しない。
+    ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default);
+}
+
+// Returns a leased image only for Success or Suboptimal.
+public readonly record struct SurfaceAcquireResult(SurfaceStatus Status, IGraphicsSurfaceFrame? Frame);
+
+// Owns one presentation image lease; semaphores are owned and selected by the caller.
+public interface IGraphicsSurfaceFrame : IDisposable
+{
+    // Gets local acquisition and presentation state; GPU submissions are not tracked.
+    SurfaceFrameStatus Status { get; }
+
+    // Gets the borrowed image; do not dispose it directly.
+    IGraphicsTexture Texture { get; }
+
+    // Requests presentation with caller-supplied synchronization; GPU submissions are not inspected.
+    SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null);
+
+    // Waits for native acquisition and presentation; wait for GPU submission separately.
+    ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default);
+}
+
+// Specifies the lifetime of one acquired presentation image.
+public enum SurfaceFrameStatus
+{
+    // The caller may record and submit use before presentation.
+    Acquired,
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
+// 未signalで生成する所有binary semaphore。利用側が使用終了を確認してDisposeする。
+public interface IGraphicsSemaphore : IDisposable { }
+
+public sealed record SemaphoreWaitDesc
+{
+    // 同じdeviceの、signalが発行済みのsemaphore。
+    public required IGraphicsSemaphore Semaphore { get; init; }
+    // GPUの待機stage。None／Host／未知bitを指定しない。
+    public required PipelineStage Stages { get; init; }
+}
+
+public sealed record QueueSubmitDesc
+{
+    // 実行順。空ならwaitまたはsignalが必須。
+    public IReadOnlyList<IGraphicsCommandBuffer> CommandBuffers { get; init; } = [];
+    // 自動追加なし。重複とsignal／waitの順序は利用側が管理する。
+    public IReadOnlyList<SemaphoreWaitDesc> WaitSemaphores { get; init; } = [];
+    // command完了後にsignalする。waitと同じsemaphoreを指定しない。
+    public IReadOnlyList<IGraphicsSemaphore> SignalSemaphores { get; init; } = [];
+}
+
 public interface IGraphicDevice
 {
+    // 未signalのbinary semaphoreを生成する。
+    IGraphicsSemaphore CreateSemaphore();
 }
 public interface IGraphicsQueue
 {
+    // 指定されたwait／signalだけを発行。Frameを引数に取らない。
+    IGraphicsSubmission Submit(QueueSubmitDesc desc);
 }
 public enum TextureState
 {
+    // 取得画像だけに許可する。Before/After scopeのAccessはNoneで提示へ解放する。
+    Present,
 }
```

Present stateのbarrierはネイティブ同期に必要な状態遷移を記録し、Present自体は実行しない。
通常のTextureにPresent stateを指定するとArgumentExceptionになる。
利用側はcapsに対応する形式・usage・modeと正の寸法を指定する。構成のたびにcapsの全照会や対応リストの再走査は行わず、局所的な引数検査とネイティブ結果の処理を行う。
所有権、GPU使用中の解放、状態遷移の履歴、同期の正しさは利用側が保証する。検証専用の追跡・走査は行わない。
現在操作するinstanceの解放済み状態や局所的な二重Presentの検査と、ネイティブエラー処理は維持する。
共通方針は[GRAPHICS-0014](GRAPHICS-0014-caller-managed-resource-validation.md)に従う。

## 検討した代替案

取得、submit、present、resizeを一つのrender loopへ自動化すると利用側のcommand発行と同期方針を制約するため採用しない。
Frameを通常の所有Textureとして返すとネイティブSwapchain画像を誤って解放できるためleaseにする。
共通のWindowHandle型を定義するとWindowingとの結合が増えるため、生成はバックエンド固有にする。
FrameをSubmitへ渡して同期を自動接続する方式は利用側のwait／signal管理を隠すため採用しない。
Present前に必ずCPUでsubmission完了を待つ方式はGPUの提示同期が可能な実装で直列化を強制するため採用しない。

## 結果と影響

既存のRenderPass、TextureView、command APIを取得画像にも使える。
表示先の生存、リサイズ、取得失敗への復旧、frameの解放を利用側が明示的に管理する必要がある。
画像のnative ownershipと同期の実現方法はバックエンドへ閉じ込める。同期オブジェクトの選択・signal／wait・再利用は利用側が管理する。
共通APIのテストは正しいlease寿命の下で再構成、局所的な二重提示の拒否、明示的なsubmitと完了待機、コピー読み戻しを確認する。
複数表示先を同時取得して色を描き分け、一括／別々のSubmitとPresent、片方だけのresize・解放、残る表示先の描画継続を検証する。
実ウインドウのハンドル取得とそのintegration testは後続PRに分離する。
