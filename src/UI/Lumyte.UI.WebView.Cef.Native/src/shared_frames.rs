//! shm-v1: the managed owner creates the fixed file; this writer never grows it.
//! Ordinary slot bytes are accessed only while holding Writing. State release/acquire
//! transfers ownership; atomic sequence loads permit concurrent Ready candidate scans.
#[cfg(not(all(
    target_os = "linux",
    target_endian = "little",
    target_has_atomic = "32",
    target_has_atomic = "64",
    any(target_arch = "x86_64", target_arch = "aarch64")
)))]
compile_error!("shm-v1 requires Linux little-endian x64/aarch64 and 32/64-bit atomics");
use std::{
    fs::{File, OpenOptions},
    io,
    os::{
        fd::AsRawFd,
        unix::fs::{MetadataExt, OpenOptionsExt},
    },
    path::Path,
    ptr::NonNull,
    sync::atomic::{AtomicU32, AtomicU64, Ordering},
};
pub const MAGIC: &[u8; 8] = b"LUMYSHM1";
const _: () = {
    assert!(std::mem::size_of::<AtomicU32>() == 4);
    assert!(std::mem::size_of::<AtomicU64>() == 8);
    assert!(std::mem::align_of::<AtomicU32>() <= 4);
    assert!(std::mem::align_of::<AtomicU64>() <= 8);
};
const HEADER: usize = 4096;
const SLOTS: usize = 3;
const FREE: u32 = 0;
const WRITING: u32 = 1;
const READY: u32 = 2;
#[cfg(test)]
const READING: u32 = 3;
unsafe extern "C" {
    fn mmap(
        addr: *mut std::ffi::c_void,
        len: usize,
        prot: i32,
        flags: i32,
        fd: i32,
        offset: i64,
    ) -> *mut std::ffi::c_void;
    fn munmap(addr: *mut std::ffi::c_void, len: usize) -> i32;
    fn geteuid() -> u32;
}
fn invalid() -> io::Error {
    io::Error::other("invalid shared frame map")
}
pub struct SharedFrames {
    base: NonNull<u8>,
    len: usize,
    capacity: usize,
    stride: usize,
    nonce: [u8; 16],
    sequence: u64,
    _file: File,
}
impl Drop for SharedFrames {
    fn drop(&mut self) {
        unsafe {
            munmap(self.base.as_ptr().cast(), self.len);
        }
    }
}
impl SharedFrames {
    pub fn open_current() -> io::Result<Self> {
        Self::open_root(&std::env::temp_dir())
    }
    pub(crate) fn open_root(root: &Path) -> io::Result<Self> {
        // Open the directory itself without following symlinks, then openat through its
        // stable descriptor. Neither a caller path nor a CEF subprocess selects a map.
        let directory = OpenOptions::new()
            .read(true)
            .custom_flags(0x20000 | 0x10000 | 0x80000)
            .open(root)?;
        let md = directory.metadata()?;
        if !md.is_dir() || md.uid() != unsafe { geteuid() } || md.mode() & 0o7777 != 0o700 {
            return Err(invalid());
        }
        let path = format!("/proc/self/fd/{}/frames-v1.shm", directory.as_raw_fd());
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .custom_flags(0x20000 | 0x80000 | 0x800)
            .open(path)?;
        let md = file.metadata()?; // fstat of the actual opened descriptor, not a path check.
        if !md.is_file() || md.uid() != unsafe { geteuid() } || md.mode() & 0o7777 != 0o600 {
            return Err(invalid());
        }
        let len = usize::try_from(md.len()).map_err(|_| invalid())?;
        if !(HEADER + SLOTS * HEADER..=HEADER + SLOTS * (16 * 1024 * 1024 + HEADER)).contains(&len)
        {
            return Err(invalid());
        }
        let ptr = unsafe { mmap(std::ptr::null_mut(), len, 3, 1, file.as_raw_fd(), 0) };
        if ptr as isize == -1 {
            return Err(io::Error::last_os_error());
        }
        let mut map = Self {
            base: NonNull::new(ptr.cast()).ok_or_else(invalid)?,
            len,
            capacity: 0,
            stride: 0,
            nonce: [0; 16],
            sequence: 0,
            _file: file,
        };
        // Header is immutable for the complete lifetime of this mapping.
        let h = unsafe { std::slice::from_raw_parts(map.base.as_ptr(), HEADER) };
        let u32_at = |i| u32::from_le_bytes(h[i..i + 4].try_into().unwrap());
        let u64_at = |i| u64::from_le_bytes(h[i..i + 8].try_into().unwrap());
        let capacity = usize::try_from(u64_at(24)).map_err(|_| invalid())?;
        let stride = capacity.checked_add(64 + 4095).ok_or_else(invalid)? & !4095;
        if &h[..8] != MAGIC
            || u32_at(8) != 1
            || u32_at(12) != 4096
            || u32_at(16) != 3
            || u32_at(20) != 64
            || capacity == 0
            || capacity > 16 * 1024 * 1024
            || capacity % 4096 != 0
            || u64_at(32) != stride as u64
            || u64_at(40) != len as u64
            || len != HEADER + SLOTS * stride
            || h[64..].iter().any(|b| *b != 0)
            || h[48..64].iter().all(|b| *b == 0)
        {
            return Err(invalid());
        }
        map.capacity = capacity;
        map.stride = stride;
        map.nonce.copy_from_slice(&h[48..64]);
        for i in 0..SLOTS {
            let p = map.slot(i);
            let s = unsafe { std::slice::from_raw_parts(p, 64) };
            if map.state(i).load(Ordering::Acquire) != FREE
                || map.frame_sequence(i).load(Ordering::Acquire) != 0
                || u32::from_le_bytes(s[4..8].try_into().unwrap()) != i as u32
                || s[16..40].iter().any(|b| *b != 0)
                || s[40..56] != map.nonce
                || u32::from_le_bytes(s[56..60].try_into().unwrap()) != 1
                || s[60..64] != [0; 4]
            {
                return Err(invalid());
            }
        }
        Ok(map)
    }
    fn slot(&self, i: usize) -> *mut u8 {
        unsafe { self.base.as_ptr().add(HEADER + i * self.stride) }
    }
    fn state(&self, i: usize) -> &AtomicU32 {
        unsafe { &*self.slot(i).cast() }
    }
    fn frame_sequence(&self, i: usize) -> &AtomicU64 {
        unsafe { &*self.slot(i).add(8).cast() }
    }
    pub fn ready_body(&self) -> Vec<u8> {
        let mut body = Vec::with_capacity(24);
        body.extend_from_slice(MAGIC);
        body.extend_from_slice(&self.nonce);
        body
    }
    pub fn publish(
        &mut self,
        generation: i64,
        width: i32,
        height: i32,
        pixels: &[u8],
    ) -> Result<bool, &'static str> {
        let stride = u32::try_from(width)
            .ok()
            .and_then(|w| w.checked_mul(4))
            .ok_or("invalid-frame-geometry")?;
        let count = usize::try_from(height)
            .ok()
            .and_then(|h| h.checked_mul(stride as usize))
            .ok_or("invalid-frame-geometry")?;
        if generation < 0 || width <= 0 || height <= 0 || count != pixels.len() {
            return Err("invalid-frame-geometry");
        }
        if count > self.capacity {
            return Err("frame-exceeds-shared-capacity");
        }
        let next = self
            .sequence
            .checked_add(1)
            .filter(|s| *s <= i64::MAX as u64)
            .ok_or("frame-sequence-exhausted")?;
        let mut claimed = None;
        // Two bounded passes. Prefer Free; otherwise reclaim only the oldest Ready.
        for _ in 0..2 {
            for i in 0..SLOTS {
                if self
                    .state(i)
                    .compare_exchange(FREE, WRITING, Ordering::AcqRel, Ordering::Acquire)
                    .is_ok()
                {
                    claimed = Some(i);
                    break;
                }
            }
            if claimed.is_some() {
                break;
            }
            let oldest = (0..SLOTS)
                .filter(|i| self.state(*i).load(Ordering::Acquire) == READY)
                .min_by_key(|i| self.frame_sequence(*i).load(Ordering::Acquire));
            if let Some(i) = oldest
                && self
                    .state(i)
                    .compare_exchange(READY, WRITING, Ordering::AcqRel, Ordering::Acquire)
                    .is_ok()
            {
                claimed = Some(i);
                break;
            }
        }
        let Some(i) = claimed else {
            return Ok(false);
        };
        let p = self.slot(i);
        unsafe {
            // No slice/reference to the full map: other slots can have active readers.
            std::ptr::copy_nonoverlapping(pixels.as_ptr(), p.add(64), count);
            std::ptr::copy_nonoverlapping(generation.to_le_bytes().as_ptr(), p.add(16), 8);
            std::ptr::copy_nonoverlapping(width.to_le_bytes().as_ptr(), p.add(24), 4);
            std::ptr::copy_nonoverlapping(height.to_le_bytes().as_ptr(), p.add(28), 4);
            std::ptr::copy_nonoverlapping(stride.to_le_bytes().as_ptr(), p.add(32), 4);
            std::ptr::copy_nonoverlapping((count as u32).to_le_bytes().as_ptr(), p.add(36), 4);
        }
        self.frame_sequence(i).store(next, Ordering::Relaxed);
        self.sequence = next;
        self.state(i).store(READY, Ordering::Release);
        Ok(true)
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use std::{io::Write, os::unix::fs::PermissionsExt, path::PathBuf, sync::atomic::AtomicUsize};
    static ID: AtomicUsize = AtomicUsize::new(0);
    pub(crate) struct Root(pub(crate) PathBuf);
    impl Root {
        pub(crate) fn new() -> Self {
            let path = std::env::temp_dir().join(format!(
                "shm-unit-{}-{}",
                std::process::id(),
                ID.fetch_add(1, Ordering::Relaxed)
            ));
            std::fs::create_dir(&path).unwrap();
            std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o700)).unwrap();
            Self(path)
        }
        pub(crate) fn create(&self, mutate: impl FnOnce(&mut [u8])) {
            let mut bytes = vec![0; 4096 + 3 * 8192];
            bytes[..8].copy_from_slice(MAGIC);
            for (offset, n) in [(8, 1u32), (12, 4096), (16, 3), (20, 64)] {
                bytes[offset..offset + 4].copy_from_slice(&n.to_le_bytes());
            }
            for (offset, n) in [(24, 4096u64), (32, 8192), (40, 28672)] {
                bytes[offset..offset + 8].copy_from_slice(&n.to_le_bytes());
            }
            bytes[48..64].copy_from_slice(&[0x42; 16]);
            for i in 0..3 {
                let p = 4096 + i * 8192;
                bytes[p + 4..p + 8].copy_from_slice(&(i as u32).to_le_bytes());
                bytes[p + 40..p + 56].copy_from_slice(&[0x42; 16]);
                bytes[p + 56..p + 60].copy_from_slice(&1u32.to_le_bytes());
            }
            mutate(&mut bytes);
            let mut f = OpenOptions::new()
                .write(true)
                .create_new(true)
                .mode(0o600)
                .open(self.0.join("frames-v1.shm"))
                .unwrap();
            f.write_all(&bytes).unwrap();
        }
        fn open(&self) -> SharedFrames {
            SharedFrames::open_root(&self.0).unwrap()
        }
    }
    impl Drop for Root {
        fn drop(&mut self) {
            std::fs::remove_dir_all(&self.0).unwrap();
        }
    }
    #[test]
    fn golden_layout_and_handshake() {
        let root = Root::new();
        root.create(|_| {});
        let mut m = root.open();
        assert_eq!(m.ready_body(), [MAGIC.as_slice(), &[0x42; 16]].concat());
        assert!(m.publish(7, 2, 1, &[0x88; 8]).unwrap());
        let slot = unsafe { std::slice::from_raw_parts(m.slot(0), 72) };
        assert_eq!(u32::from_le_bytes(slot[0..4].try_into().unwrap()), READY);
        assert_eq!(&slot[8..16], &1u64.to_le_bytes());
        assert_eq!(&slot[16..24], &7i64.to_le_bytes());
        assert_eq!(
            &slot[24..40],
            &[2, 0, 0, 0, 1, 0, 0, 0, 8, 0, 0, 0, 8, 0, 0, 0]
        );
        assert_eq!(&slot[64..72], &[0x88; 8]);
        assert_eq!(m.state(0) as *const _ as usize % 4, 0);
        assert_eq!(m.frame_sequence(0) as *const _ as usize % 8, 0);
    }
    #[test]
    fn held_reading_immutable_oldest_ready_reclaimed_and_full_drop() {
        let root = Root::new();
        root.create(|_| {});
        let mut m = root.open();
        for _ in 0..3 {
            assert!(m.publish(1, 1, 1, &[1; 4]).unwrap());
        }
        assert_eq!(
            m.state(0)
                .compare_exchange(READY, READING, Ordering::AcqRel, Ordering::Acquire),
            Ok(READY)
        );
        let held = unsafe { std::slice::from_raw_parts(m.slot(0).add(4), 64).to_vec() };
        for _ in 0..1000 {
            assert!(m.publish(2, 1, 1, &[2; 4]).unwrap());
        }
        assert_eq!(held, unsafe {
            std::slice::from_raw_parts(m.slot(0).add(4), 64)
        });
        assert_eq!(m.frame_sequence(0).load(Ordering::Acquire), 1);
        assert_eq!(m.frame_sequence(1).load(Ordering::Acquire), 1002);
        assert_eq!(m.frame_sequence(2).load(Ordering::Acquire), 1003);
        for i in 0..3 {
            m.state(i).store(READING, Ordering::Release);
        }
        let seq = m.sequence;
        assert!(!m.publish(2, 1, 1, &[3; 4]).unwrap());
        assert_eq!(seq, m.sequence);
        m.state(0).store(FREE, Ordering::Release);
        assert!(m.publish(3, 1, 1, &[3; 4]).unwrap());
    }
    #[test]
    fn malformed_headers_and_slots_rejected() {
        for offset in [
            0, 8, 12, 16, 20, 24, 32, 40, 64, 4096, 4100, 4104, 4112, 4136, 4152, 4156,
        ] {
            let root = Root::new();
            root.create(|b| b[offset] ^= 0xff);
            assert!(SharedFrames::open_root(&root.0).is_err(), "offset {offset}");
        }
        for offset in [24, 32, 40] {
            let root = Root::new();
            root.create(|b| b[offset..offset + 8].fill(255));
            assert!(SharedFrames::open_root(&root.0).is_err());
        }
        let root = Root::new();
        root.create(|b| b[48..64].fill(0));
        assert!(SharedFrames::open_root(&root.0).is_err());
    }
    #[test]
    fn permissions_symlinks_and_size_rejected() {
        let root = Root::new();
        root.create(|_| {});
        let file = root.0.join("frames-v1.shm");
        std::fs::set_permissions(&file, std::fs::Permissions::from_mode(0o644)).unwrap();
        assert!(SharedFrames::open_root(&root.0).is_err());
        std::fs::set_permissions(&file, std::fs::Permissions::from_mode(0o600)).unwrap();
        let other = root.0.join("other");
        std::fs::rename(&file, &other).unwrap();
        std::os::unix::fs::symlink(&other, &file).unwrap();
        assert!(SharedFrames::open_root(&root.0).is_err());
        std::fs::remove_file(&file).unwrap();
        std::fs::rename(&other, &file).unwrap();
        OpenOptions::new()
            .write(true)
            .open(file)
            .unwrap()
            .set_len(4096)
            .unwrap();
        assert!(SharedFrames::open_root(&root.0).is_err());
    }
    #[test]
    fn oversize_and_sequence_overflow_leave_slots_untouched() {
        let root = Root::new();
        root.create(|_| {});
        let mut m = root.open();
        assert_eq!(
            m.publish(1, 1025, 1, &[0; 4100]),
            Err("frame-exceeds-shared-capacity")
        );
        m.sequence = i64::MAX as u64;
        assert_eq!(m.publish(1, 1, 1, &[0; 4]), Err("frame-sequence-exhausted"));
        for i in 0..3 {
            assert_eq!(m.state(i).load(Ordering::Acquire), FREE);
        }
    }
    // The consumer is a separate invocation of this test executable. It maps the
    // same file independently: no inherited Rust pointers or CEF initialization.
    #[test]
    fn cross_process_consumer_entry() {
        let Some(root) = std::env::var_os("LUMYTE_SHM_UNIT_ROOT") else {
            return;
        };
        let map = SharedFrames::open_root(Path::new(&root)).unwrap();
        std::fs::write(Path::new(&root).join("consumer-ready"), []).unwrap();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
        let mut accepted = 0;
        let mut last = 0;
        loop {
            for i in 0..SLOTS {
                if map
                    .state(i)
                    .compare_exchange(READY, READING, Ordering::AcqRel, Ordering::Acquire)
                    .is_err()
                {
                    continue;
                }
                let sequence = map.frame_sequence(i).load(Ordering::Acquire);
                let slot = unsafe { std::slice::from_raw_parts(map.slot(i), 64) };
                let generation = i64::from_le_bytes(slot[16..24].try_into().unwrap());
                let pixels = unsafe { std::slice::from_raw_parts(map.slot(i).add(64), 4096) };
                assert_eq!(generation, sequence as i64);
                assert!(pixels.iter().all(|v| *v == sequence as u8));
                std::thread::yield_now();
                assert!(pixels.iter().all(|v| *v == sequence as u8));
                if sequence > last {
                    last = sequence;
                    accepted += 1;
                }
                map.state(i).store(FREE, Ordering::Release);
            }
            if Path::new(&root).join("producer-done").exists() {
                break;
            }
            assert!(std::time::Instant::now() < deadline, "producer deadline");
            std::thread::yield_now();
        }
        assert!(accepted > 0);
    }
    #[test]
    fn cross_process_producer_consumer_race() {
        let root = Root::new();
        root.create(|_| {});
        let mut map = root.open();
        let mut child = std::process::Command::new(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "shared_frames::tests::cross_process_consumer_entry",
                "--nocapture",
            ])
            .env("LUMYTE_SHM_UNIT_ROOT", &root.0)
            .spawn()
            .unwrap();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
        while !root.0.join("consumer-ready").exists() {
            if std::time::Instant::now() >= deadline {
                let _ = child.kill();
                let _ = child.wait();
                panic!("consumer startup deadline");
            }
            std::thread::yield_now();
        }
        for _ in 0..20000 {
            let next = map.sequence + 1;
            map.publish(next as i64, 32, 32, &[next as u8; 4096])
                .unwrap();
            if map.sequence.is_multiple_of(16) {
                std::thread::yield_now();
            }
        }
        std::fs::write(root.0.join("producer-done"), []).unwrap();
        loop {
            if let Some(status) = child.try_wait().unwrap() {
                assert!(status.success());
                break;
            }
            if std::time::Instant::now() >= deadline {
                let _ = child.kill();
                let _ = child.wait();
                panic!("consumer exit deadline");
            }
            std::thread::sleep(std::time::Duration::from_millis(1));
        }
    }
    #[test]
    fn publication_performs_no_heap_allocation() {
        let root = Root::new();
        root.create(|_| {});
        let mut map = root.open();
        crate::allocation_probe::COUNT.with(|v| v.set(0));
        crate::allocation_probe::ENABLED.with(|v| v.set(true));
        for _ in 0..1000 {
            assert!(map.publish(1, 32, 32, &[5; 4096]).unwrap());
        }
        crate::allocation_probe::ENABLED.with(|v| v.set(false));
        assert_eq!(crate::allocation_probe::COUNT.with(|v| v.get()), 0);
    }
}
