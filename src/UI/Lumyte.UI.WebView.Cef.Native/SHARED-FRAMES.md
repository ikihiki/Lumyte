# Shared CPU frames: shm-v1

The Linux backend uses a fixed three-slot mapping for BGRA8 pixels. Pipes carry
commands, state and small notifications only; pixel bytes never travel in frame
packets. There is no automatic full-pixel pipe fallback. This is not GPU sharing
or zero-copy: CEF's borrowed callback buffer is copied into a mapped slot, then
into the existing budgeted managed pixel mailbox before its slot is released.

## File and immutable header

The managed owner exclusively creates frames-v1.shm under the existing private
0700 TMP root with mode0600 before launching the helper. No global shared-memory
name, arbitrary mapping path, transferred pointer or descriptor-passing API is
exposed. The mapping is fixed-size for the entire session; resize changes frame
metadata, never a live file length. The caller and same-UID environment are
trusted, as with the supervisor's private root contract.

All fields are little-endian. Only Linux x64/aarch64 with aligned lock-free
32-bit and 64-bit atomics are supported. The global header occupies4096 bytes:

- 0: ASCII LUMYSHM1,8 bytes;8: u32 version1;12: u32 header4096.
- 16: u32 slotCount3;20: u32 slotHeader64.
- 24: u64 capacityBytes;32: u64 slotStrideBytes;40: u64 fileBytes.
- 48: cryptographically random session nonce,16 bytes;64..4095: reserved zero.

slotStrideBytes is AlignUp(64+capacityBytes,4096). fileBytes is
4096+3*slotStrideBytes. Slot i starts at4096+i*slotStrideBytes; pixels start
64 bytes later. A slot header contains:

- 0: atomic u32 state;4: u32 slot index;8: atomic u64 frame sequence.
- 16: i64 viewport generation;24/28: i32 width/height.
- 32: u32 byte stride;36: u32 pixel byte count.
- 40: session nonce,16 bytes;56: u32 format1 (packed BGRA8);60: reserved zero.

States are Free0, Writing1, Ready2 and Reading3. Initial states/sequences are zero.
Published frame sequences are positive and never wrap; they are independent of
control-packet event sequences. Ordinary metadata is not read before claiming
exclusive slot ownership.

## Ownership and notification

The producer atomically claims a Free slot, otherwise an old Ready slot. It
never overwrites Reading. It copies valid borrowed pixels directly into mapped
storage, completes metadata, then publishes Ready with release ordering. If all
slots are held, it drops that frame without blocking the CEF owner.

The consumer scans atomic state/sequence, claims a latest candidate with
Ready-to-Reading CAS, and rereads sequence and validates nonce, index, generation,
format, reserved fields, checked dimensions/stride/byte count and mapped bounds
while exclusively leased. Selection retries are bounded and best-effort under
concurrent publication. It copies through the existing managed mailbox and
releases Free in finally, including stale-generation and error paths. Existing
scene/frame/upload leases continue to govern the owned mailbox image.

Ready101 carries exactly LUMYSHM1 plus the session nonce (24 bytes). Old helpers
without this capability are rejected. FrameNotice103 has an empty body: its
entire wire packet is44 bytes. The slot metadata, not an older queued notice's
metadata, identifies the consumed frame. Control bodies remain at most4096 bytes;
a legacy frame-sized body is rejected before allocation.

The producer publishes Ready before trying to enqueue a notice. Frame-notice
queue saturation drops that notice only; the latest Ready slot remains. The
consumer rescans after every valid packet, including initial Ready and state
packets, so queued state traffic cannot lose the last static frame notification.
State-queue saturation and disconnected IPC remain faults. No pending-bit
coalescing relies on a future paint to repair a lost wakeup.

## Budget and shutdown

Capacity is derived from the actual injected WebViewPixelBudget, not merely the
options default. Include the mapping header, slot headers and page padding. A
bounded control allowance and headroom for owned pixel leases constrain slot
capacity; atomically reserve mapping ownership before allocating. The existing
mailbox/upload leases still reserve their own bytes. Capacity shortage is a
browser-local failure, not a live-map expansion or an unaccounted allocation.
Start snapshots a fitting pending viewport before native browser creation. If
layout has not arrived and the default800x600 cannot fit, a bounded1x1 bootstrap
is used without advancing the caller-owned generation. Its pixels are suppressed
until SetViewport installs the requested geometry, rather than faulting on an
unrequested oversized initial paint. Explicit oversized viewports still fail.

Reader leases and native users must quiesce before unmapping and releasing the
reservation. A cancellation request or cleanup timeout alone is not retirement
proof. StopFailed retains the same outstanding process/root/workflow ownership
for retry. The supervisor unlinks the owned root only after native descendants
are reaped; Linux mappings remain valid across unlink until their handles close.
