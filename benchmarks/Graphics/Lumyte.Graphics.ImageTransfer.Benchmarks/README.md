# D06 images / G03 transfers: diagnostic benchmarks

This integrated executable uses real images and fixed-byte transfer round trips.
See [verification](../VERIFICATION.md) for strict builds and correctness gates.
Short diagnostic runs are not formal performance comparisons.

## Build and smoke (repository root)

    dest=benchmarks/Graphics/Lumyte.Graphics.ImageTransfer.Benchmarks
    mkdir -p "$dest"

    dotnet build "$dest" -c Release
    dotnet run --no-build -c Release --project "$dest" -- --smoke

The project is intended for this final destination, not direct execution from its
scratch path. It links only core quarantine/provenance/frame/command/timer helpers,
not the legacy executable. Hardware timer validates actual Radeon8060S1002:1586;
other hardware/software Vulkan is not supported by the inherited adapter guard.

## Representative hardware commands

    dotnet run --no-build -c Release --project "$dest" -- --scenario D06 --resource atlas --updates resident --order alternating --depth 1 --frames 6 --output artifacts/graphics-benchmarks/D06-atlas-resident
    dotnet run --no-build -c Release --project "$dest" -- --scenario D06 --resource distinct --updates resident --order grouped --depth 3 --frames 6 --output artifacts/graphics-benchmarks/D06-distinct-grouped
    dotnet run --no-build -c Release --project "$dest" -- --scenario D06 --resource atlas --updates upload --order alternating --depth 3 --frames 6 --output artifacts/graphics-benchmarks/D06-atlas-upload
    dotnet run --no-build -c Release --project "$dest" -- --scenario G03 --resource buffer --updates contiguous --depth 1 --frames 6 --output artifacts/graphics-benchmarks/G03-buffer-contiguous
    dotnet run --no-build -c Release --project "$dest" -- --scenario G03 --resource texture --updates dispersed --depth 3 --frames 6 --output artifacts/graphics-benchmarks/G03-texture-dispersed

D06 supports atlas/distinct x resident/upload x alternating/grouped x depth1/3.
G03 supports buffer/texture x contiguous/dispersed x depth1/3. At least3frames
required, maximum100000. Unknown/duplicate/incomplete options reject before device
creation; G03 rejects --order rather than silently accepting an irrelevant parameter.
--smoke is managed fixture validation only, not GPU coverage. Serialize builds/tests
and GPU timing; no formal comparison claims from these short diagnostic runs.

## D06 real resident images, ordering and upload variants

-256 sprites,16x16texels each, fixed opaque 4x4texel checker cells. Four base colors
 red/green/blue/yellow alternate with white. Textures use Rgba8Srgb, nearest sampling,
 no mip filtering/anisotropy tricks, actual Image2D and Canvas.DrawImage.
-Atlas is one64x16texture with four adjacent tiles; distinct uses four16x16textures.
 Each slot owns its texture/view/sampler/upload resources. Depth3 therefore owns
 three independent image sets in BOTH variants, not one globally shared resident set.
-Logical sprite i always occupies the same visible16x16pixel destination at pitch32.
 Alternating visits i0..255 (texture type i%4). Grouped visits all type0, then1/2/3,
 preserving each logical destination. Sprites do not overlap: exact output remains
 identical across resource ordering; painter order is not illegally reordered in a
 transparent workload. This specifically isolates resource ordering for opaque sprites.
-The completed DrawList is created once per slot and reused in both modes. Resident
 upload/setup occurs before measurements and measured upload bytes are0. Upload mode
 toggles checker texels every logical frame, records real copy commands and uploads
 all four logical tiles:4096GPU payload bytes per frame (atlas1copy/distinct4copies).
 Actual CPU mapped bytes may include row padding and are reported separately.
-No file decode, OS texture discovery, fake image replacements or product API changes.
 Payload arrays and both state hashes are prepared before timing; source generation
 is not charged to upload. Map/CopyFrom/Unmap and GPU upload synchronization ARE timed.
-The image upload queue submission is awaited before preparing/drawing that slot.
 UploadHostMs includes mapping/copy, encode, submit and upload fence; UploadGpuMs is
 an independent timestamp around upload barriers+copies, excluding CPU mapping.
 DrawGpuMs is separately recorded in row.GpuMs. Do not sum overlapping GPU timestamps
 as device-exclusive busy time or call completion latency GPU execution time.

D06 independently constructs the reference as4096 colored Canvas FillRect commands
from logical checker coordinates, not by sampling/replaying the image source. Every
RGBA target byte must agree with the256 DrawImage result.512known interior checker
colors are additionally asserted in BOTH paths. Both upload states are preflighted
and their actual rendered hashes must differ, proving real visible texture updates.
Analytic endpoint colors require exact bytes, no relaxed image comparison threshold.
Every final in-flight target is compared to its original logical frame ID/state.

## G03 fixed byte roundtrips and locality

-Total resource65536bytes; per measured frame GPU upload16384bytes, full GPU readback
65536bytes. Four4096byte uploads plus one complete readback copy =>5actual copy calls
in BOTH contiguous/dispersed cases. Byte counts are literal issued regions, not an
inferred bandwidth estimate. This is roundtrip traffic, not upload-only GPU timing.
-Buffer:65536byte automatic-memory resource. Contiguous blocks0/1/2/3; dispersed
blocks0/4/8/12. GPU buffer offset/size requirements are checked against DeviceCaps;
unsupported fixed profiles throw rather than round or silently shrink the payload.
-Texture:128x128Rgba8Srgb, raw byte copies (no format conversion). Four8-row bands,
contiguous row0/8/16/24 or dispersed row0/32/64/96. Each band4096logical texel bytes.
Copy row pitch is aligned to the LCM of DeviceCaps.CopyBytesPerRowAlignment and the
format-specific GetTextureCopyLayout result. Buffer offset alignment is explicitly
checked. Payload bytes exclude row padding; mapped buffers include it. Actual per-slot
BytesPerRow is reported. Full readback is depadded before exact byte comparison.
-Initial destination is explicitly uploaded as all-zero and roundtrip-checked outside
timing. Selected bytes toggle0x39/0xC7 each frame; untouched bytes must remain zero.
CPU Map/CopyFrom writes the entire padded source buffer each frame, intentionally
fixed across locality variants: MappedBytes is NOT the GPU upload count.
-Each depth1/3slot has upload, destination, readback, command, submission and native
query pool. Slot wait completes the old fence/query/command ownership before writing
its upload memory again. GPU timestamp spans the copy barriers, upload copies and
full readback copy including the final host-read barrier. CPU mapping/host CopyFrom
is separately MapCopyMs. GPU time is null if capability is unavailable, never replaced
with fence wait duration. Invalid queries fail, never silently become0.

Preflight checks both nonzero byte states, plus the initialized zero state. Final
readback for each slot is compared byte-for-byte with the original source trace.
The complete65536bytes, including untouched regions, must match. Input IDs are state
hashes independent of slot/retirement order. No apparent fast path can skip a copy
while leaving the wrong state unnoticed in these checks.

## Stage fields, evidence and lifetime

manifest.json records actual device/driver, DeviceCaps, format copy layout, runtime,
scenario/version/options, canonical image or transfer state hashes, source/shader/
output-binary hashes from core provenance. frames.jsonl contains original frame/input
IDs, exact uploaded/readback/mapped byte counts, actual copy and forwarded draw calls,
map-copy/prepare/encode/submit/host/slot-wait/completion and separate GPU spans. Images
use public Canvas rendering, not retained Scene compilation; no cache-hit or eviction
counters are invented for this suite. checks.jsonl contains exact reference evidence;
summary.json declares diagnostic use and final-drain-inclusive headless throughput.

Preflight and JSON/reference construction/readback checks are outside the measured
interval. Sample objects, state payloads, IDs and query-result lists are allocated
before measurement. Throughput elapsed includes final completion and command/resource
retirement; per-stage completion is submit-to-observed-fence host latency and includes
queue delay/late observation. Slot-wait includes preceding retirement. Dedicated
retirement allocation/time attribution is not supplied by this staged runner.
No forced GC or image decode occurs during measurements.

Texture update resources are never reused while their draw slot is pending. Failed
or unknown submission ownership quarantines commands, resource owners and device
until process exit, rather than freeing resources under potentially live GPU work.
Failures write failed summaries/checks and invalidate comparison. Device-creation or
adapter-guard failures occur before full manifest completion; preserve console/error
logs as capability failures. Build success is not GPU proof.

## Honest remaining gaps

Diagnostic only: no cold/pilot/formal process orchestration, CPU BenchmarkDotNet job,
long-run distributions, upload ring suballocation, resource retirement memory graphs,
compressed/float texture formats, texture scaling/filter/alpha families, atlas border
bleed stress, GPU upload/draw overlap optimization or global resident image sharing.
No shader/API profiler upload bytes beyond explicitly issued copy regions, no native
VRAM or driver-combined draw counter. Existing other ADR scenarios are unchanged.
Run all supported profiles after integration before marking any of them validated.
