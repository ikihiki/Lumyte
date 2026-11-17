//! Linux-only browser-local cleanup owner. Never loads CEF or relays protocol IO.
use std::{
    env, fs, io,
    os::{
        fd::{AsRawFd, FromRawFd, OwnedFd},
        unix::{ffi::OsStrExt, fs::MetadataExt, process::CommandExt},
    },
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::atomic::{AtomicI32, Ordering},
    thread,
    time::{Duration, Instant},
};

unsafe extern "C" {
    fn getppid() -> i32;
    fn geteuid() -> u32;
    fn prctl(option: i32, ...) -> i32;
    fn syscall(number: i64, ...) -> i64;
    fn waitpid(pid: i32, status: *mut i32, options: i32) -> i32;
    fn close(fd: i32) -> i32;
    fn signal(sig: i32, handler: extern "C" fn(i32)) -> usize;
    fn poll(fds: *mut PollFd, count: usize, timeout: i32) -> i32;
}
#[repr(C)]
struct PollFd {
    fd: i32,
    events: i16,
    revents: i16,
}
static STOP: AtomicI32 = AtomicI32::new(0);
extern "C" fn stopped(sig: i32) {
    STOP.store(sig, Ordering::Relaxed);
}
fn pidfd(pid: i32) -> io::Result<OwnedFd> {
    let fd = unsafe { syscall(434, pid, 0_u32) } as i32;
    if fd < 0 {
        Err(io::Error::last_os_error())
    } else {
        Ok(unsafe { OwnedFd::from_raw_fd(fd) })
    }
}
fn send(fd: &OwnedFd, sig: i32) -> io::Result<()> {
    if unsafe { syscall(424, fd.as_raw_fd(), sig, std::ptr::null::<u8>(), 0_u32) } < 0 {
        Err(io::Error::last_os_error())
    } else {
        Ok(())
    }
}
fn dead(fd: &OwnedFd) -> bool {
    let mut p = PollFd {
        fd: fd.as_raw_fd(),
        events: 1,
        revents: 0,
    };
    unsafe { poll(&mut p, 1, 0) > 0 }
}
struct Options {
    owner: i32,
    root: PathBuf,
    stop: PathBuf,
    command: Vec<std::ffi::OsString>,
    portal: bool,
}
fn options() -> Result<Options, String> {
    let mut a: Vec<_> = env::args_os().skip(1).collect();
    let portal = a.first().is_some_and(|s| s == "--portal-session");
    if portal {
        a.remove(0);
    }
    if a.len() < 9 || a[0] != "--owner" || a[2] != "--root" || a[4] != "--stop" || a[6] != "--" {
        return Err("expected [--portal-session] --owner PID --root ROOT --stop ROOT/stop -- HELPER --lumyte-helper [--lumyte-allow-unsandboxed]".into());
    }
    let owner = a[1]
        .to_str()
        .and_then(|s| s.parse::<i32>().ok())
        .filter(|p| *p > 1)
        .ok_or("invalid owner")?;
    let root = PathBuf::from(&a[3]);
    let stop = PathBuf::from(&a[5]);
    let command = a[7..].to_vec();
    if !(command.len() == 2 || command.len() == 3 && command[2] == "--lumyte-allow-unsandboxed")
        || command[1] != "--lumyte-helper"
        || !Path::new(&command[0]).is_absolute()
    {
        return Err("invalid helper command".into());
    }
    validate_root(&root).map_err(|e| e.to_string())?;
    let expected = root.join(if portal { "isolated-stop" } else { "stop" });
    if stop != expected {
        return Err("invalid stop marker".into());
    }
    if portal && !Path::new(&command[0]).is_file() {
        return Err("missing portal launcher".into());
    }
    Ok(Options {
        owner,
        root,
        stop,
        command,
        portal,
    })
}
fn validate_root(root: &Path) -> io::Result<()> {
    let m = fs::symlink_metadata(root)?;
    if !root.is_absolute()
        || root.as_os_str().as_bytes().len() > 59
        || !root
            .file_name()
            .is_some_and(|s| s.as_bytes().starts_with(b"lumyte-browser-"))
        || !m.is_dir()
        || m.uid() != unsafe { geteuid() }
        || m.mode() & 0o7777 != 0o700
        || fs::canonicalize(root)? != root
    {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "absolute non-symlink owned 0700 lumyte-browser root (<=59 bytes) required",
        ));
    }
    Ok(())
}
fn children() -> io::Result<Vec<i32>> {
    fs::read_to_string(format!("/proc/self/task/{}/children", std::process::id())).map(|s| {
        s.split_whitespace()
            .filter_map(|p| p.parse().ok())
            .collect()
    })
}
fn reap(primary: i32, result: &mut Option<i32>) -> io::Result<()> {
    loop {
        let mut status = 0;
        let pid = unsafe { waitpid(-1, &mut status, 1) };
        if pid == primary {
            *result = Some(if status & 0x7f == 0 {
                (status >> 8) & 255
            } else {
                128 + (status & 0x7f)
            });
        }
        if pid == 0 {
            return Ok(());
        }
        if pid < 0 {
            let e = io::Error::last_os_error();
            return if e.raw_os_error() == Some(10) {
                Ok(())
            } else if e.raw_os_error() == Some(4) {
                continue;
            } else {
                Err(e)
            };
        }
    }
}
fn cleanup(primary: i32, result: &mut Option<i32>) -> io::Result<()> {
    let started = Instant::now();
    loop {
        reap(primary, result)?;
        let owned = children()?;
        if owned.is_empty() {
            return Ok(());
        }
        if started.elapsed() > Duration::from_secs(8) {
            return Err(io::Error::new(
                io::ErrorKind::TimedOut,
                "owned children not reaped; root retained",
            ));
        }
        for pid in owned {
            match pidfd(pid) {
                Ok(fd) => {
                    if children()?.contains(&pid)
                        && let Err(e) = send(
                            &fd,
                            if started.elapsed() < Duration::from_millis(500) {
                                15
                            } else {
                                9
                            },
                        )
                        && e.raw_os_error() != Some(3)
                    {
                        return Err(e);
                    }
                }
                Err(e) if e.raw_os_error() == Some(3) => (),
                Err(e) => return Err(e),
            }
        }
        thread::sleep(Duration::from_millis(10));
    }
}
fn encoded_address(path: &Path) -> String {
    let mut s = String::from("unix:path=");
    for &b in path.as_os_str().as_bytes() {
        if b.is_ascii_alphanumeric() || b"/_-.".contains(&b) {
            s.push(b as char);
        } else {
            s.push_str(&format!("%{b:02X}"));
        }
    }
    s
}
fn run(o: Options) -> io::Result<i32> {
    let root_identity = fs::symlink_metadata(&o.root)?;
    if unsafe { getppid() } != o.owner {
        return Err(io::Error::other("owner mismatch before pidfd"));
    }
    let owner = pidfd(o.owner)?;
    if unsafe { getppid() } != o.owner || dead(&owner) {
        return Err(io::Error::other("owner mismatch after pidfd"));
    }
    if unsafe { prctl(36, 1_i32, 0_i32, 0_i32, 0_i32) } != 0 {
        return Err(io::Error::last_os_error());
    }
    send(&pidfd(std::process::id() as i32)?, 0)?;
    for sig in [15, 2, 1] {
        unsafe {
            signal(sig, stopped);
        }
    }
    let mut command;
    let socket = o.root.join("portal-runtime/bus");
    if o.portal {
        if socket.as_os_str().as_bytes().len() >= 104 {
            return Err(io::Error::other("private bus path too long"));
        }
        let address = encoded_address(&socket);
        let config = o.root.join("private-bus.conf");
        fs::write(
            &config,
            format!(
                r#"<busconfig><type>session</type><listen>{address}</listen><auth>EXTERNAL</auth><policy context="default"><allow send_destination="*"/><allow receive_sender="*"/><allow own="*"/></policy></busconfig>"#
            ),
        )?;
        let log = fs::File::create(o.root.join("private-bus.log"))?;
        // dbus-run-session retains inherited fd9 while waiting; launch the bus
        // directly so only the actual helper can hold the protocol writer.
        let shell_log = log.try_clone()?;
        let mut bus = Command::new("dbus-daemon");
        bus.args(["--nofork", "--nopidfile"])
            .arg(format!("--config-file={}", config.display()))
            .stdin(Stdio::null())
            .stdout(Stdio::from(log.try_clone()?))
            .stderr(Stdio::from(log.try_clone()?));
        unsafe {
            bus.pre_exec(|| {
                close(0);
                close(8);
                close(9);
                Ok(())
            });
        }
        let bus_child = bus.spawn()?;
        let bus_pid = bus_child.id() as i32;
        let bus_fd = match pidfd(bus_pid) {
            Ok(fd) => fd,
            Err(e) => {
                cleanup(0, &mut None)?;
                return Err(e);
            }
        };
        let deadline = Instant::now() + Duration::from_secs(3);
        while !socket.exists() {
            if dead(&bus_fd)
                || dead(&owner)
                || o.stop.exists()
                || STOP.load(Ordering::Relaxed) != 0
                || Instant::now() >= deadline
            {
                cleanup(0, &mut None)?;
                return Err(io::Error::other(
                    "private bus startup interrupted or timed out",
                ));
            }
            thread::sleep(Duration::from_millis(10));
        }
        command = Command::new("/bin/bash");
        command
            .arg(&o.command[0])
            .arg("--private-session")
            .args(&o.command[1..])
            .env("DBUS_SESSION_BUS_ADDRESS", address)
            .stdout(Stdio::from(shell_log))
            .stderr(Stdio::from(log));
    } else {
        command = Command::new(&o.command[0]);
        command.args(&o.command[1..]);
    }
    // Command inherits fd0/fd1 directly, plus non-CLOEXEC fd8/fd9 in portal mode.
    command.stdin(Stdio::inherit());
    if !o.portal {
        command.stdout(Stdio::inherit()).stderr(Stdio::inherit());
    }
    let mut result = None;
    let child = command.spawn();
    unsafe {
        close(0);
        close(1);
        if o.portal {
            close(8);
            close(9);
        }
    }
    let primary = match child {
        Ok(c) => c.id() as i32,
        Err(e) => {
            eprintln!("cef-supervisor: helper launch: {e}");
            0
        }
    };
    if primary == 0 {
        result = Some(127);
    }
    while result.is_none() && STOP.load(Ordering::Relaxed) == 0 && !dead(&owner) && !o.stop.exists()
    {
        reap(primary, &mut result)?;
        if result.is_none() {
            thread::sleep(Duration::from_millis(10));
        }
    }
    let code = result.unwrap_or_else(|| {
        let sig = STOP.load(Ordering::Relaxed);
        if sig == 0 { 143 } else { 128 + sig }
    });
    cleanup(primary, &mut result)?;
    validate_root(&o.root)?;
    let current_root = fs::symlink_metadata(&o.root)?;
    if current_root.dev() != root_identity.dev() || current_root.ino() != root_identity.ino() {
        return Err(io::Error::other("private root identity changed; retained"));
    }
    if o.portal {
        match fs::remove_file(socket) {
            Ok(()) => (),
            Err(e) if e.kind() == io::ErrorKind::NotFound => (),
            Err(e) => return Err(e),
        }
    } else {
        fs::remove_dir_all(&o.root)?;
    }
    Ok(code)
}
fn main() {
    let o = match options() {
        Ok(o) => o,
        Err(e) => {
            eprintln!("cef-supervisor: {e}");
            std::process::exit(2);
        }
    };
    let code = match run(o) {
        Ok(code) => code,
        Err(e) => {
            eprintln!("cef-supervisor: cleanup/startup failure: {e}");
            125
        }
    };
    std::process::exit(code);
}
