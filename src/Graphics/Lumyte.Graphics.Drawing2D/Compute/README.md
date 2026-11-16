# GPU tiled execution

ComputeExecutionPlan2D encodes raw curve controls, immutable paint records and CPU-normalized clip membership. ComputeGeometryPlan2D uses binary64 second-difference bounds with fixed subdivision reservations; it does not expand edges on CPU.

ComputeExecution2D owns upload and writable frame-private storage. Eleven explicit dispatches initialize, generate curves, expand strokes, count/scan/fill tile references, propagate backdrop sample rows, count/scan/fill ascending draw candidates, and shade. Output is float32 premultiplied color; each draw is averaged over the correlated 4x4 grid before scene SrcOver. Ordinary graphics resolve uses SrcOver into the caller attachment, preserving Load background. No storage textures or submit/wait are required.

The baseline implementation uses serial per-draw stroke expansion and serial prefix scans. This is functional algorithm separation, not a performance claim. Inputs outside checked curve, geometry, tile reference, binding, logical-coordinate and working-byte reservations fail explicitly. Ten storage bindings are required for compute; typical WebGPU's seven usable binding budget is insufficient and is explicitly rejected, without another renderer fallback.

The graph adapter registers batch-owned prepared buffers as non-observable multi-pass preparation resources. This differs from single-pass-confined UseLocalBuffer: dependencies and uninitialized reads are checked across stages, and unused image producers are culled. Owners survive renderer disposal until graph plan retirement.

Retained frames are completion-safe but currently recompile immutable inputs. Cross-frame immutable CPU geometry/paint reuse and resource-factory/DI backend selection are not yet implemented. Test coverage and GPU validation must be reported separately from encoding/ABI tests; shader compilation alone is not image proof.
