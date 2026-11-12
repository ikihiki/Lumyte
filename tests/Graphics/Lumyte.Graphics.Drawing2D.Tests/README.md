# Drawing2Dの検証

CPU側の不変snapshot、入力検証、Canvasの状態とlayer境界、DI登録、エフェクトシステムへの接続を検証する。共通エフェクト自体は `Lumyte.Graphics.ImageEffects.Tests` で独立して検証する。

```bash
source tools/setup/activate.sh
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release
```

GPU描画は `Lumyte.Graphics.Tests/Drawing2DTests.cs` でVulkanとwgpuのreadbackを比較する。ソフトウェアVulkan driverでも実行できる。

```bash
LUMYTE_GRAPHICS_GPU_TESTS=1 VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" \
  dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter Drawing2DTests
```

通常CanvasとレイヤーCanvasの型・assembly依存を分離し、RenderGraph非依存の直接記録、
親graphへのAppend2D、Effectsコールバックの遅延呼び出しと失敗時解放を検証します。

## Exact allocation guards

Scissor/streaming allocation guards run in the existing nonparallel Drawing profiling collection. The measured workload and exact zero-byte assertion are retained. Each counter interval reserves a bounded no-GC region and asserts reservation success; disposal always ends the region. The reservation is not a byte tolerance. An escaping, non-inlined positive allocation control verifies that actual allocations remain visible.

This isolates the counter from demonstrated .NET background-GC allocation-context tail bookkeeping. It does not establish that every earlier platform failure had that cause. Nonempty clip traversal uses ordered indexing rather than an allocating interface enumerator. Diagnostics label command/clip branch, thread and architecture outside the measured interval.
