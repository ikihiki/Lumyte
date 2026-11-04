namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed record AtlasSample(string Role, double ShapeLayoutHostMs, double? AtlasCreateAndStageHostMs, double? UploadEncodeHostMs, double? UploadSubmitHostMs, double? UploadWaitHostMs, double? UploadGpuMs, uint? AtlasWidth, uint? AtlasHeight, ulong? AtlasTexelPayloadBytes, int Outlines, int VectorColorPaints);
