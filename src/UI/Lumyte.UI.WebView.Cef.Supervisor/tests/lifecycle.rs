use std::{
    fs,
    os::unix::fs::PermissionsExt,
    path::PathBuf,
    process::{Command, Stdio},
    thread,
    time::{Duration, Instant},
};
fn root() -> PathBuf {
    let p = PathBuf::from(format!("/tmp/lumyte-browser-test-{}", std::process::id()));
    fs::create_dir(&p).unwrap();
    fs::set_permissions(&p, fs::Permissions::from_mode(0o700)).unwrap();
    p
}
fn wait(c: &mut std::process::Child) -> i32 {
    let deadline = Instant::now() + Duration::from_secs(12);
    loop {
        if let Some(s) = c.try_wait().unwrap() {
            return s.code().unwrap();
        }
        assert!(Instant::now() < deadline, "bounded native lifecycle");
        thread::sleep(Duration::from_millis(10));
    }
}
#[test]
fn serialized_native_lifecycle() {
    let supervisor = env!("CARGO_BIN_EXE_lumyte-cef-supervisor");
    let base = std::env::temp_dir().join(format!("supervisor-fixtures-{}", std::process::id()));
    fs::create_dir(&base).unwrap();
    let helper = base.join("helper");
    for (script, expected) in [
        ("exit 0", 0),
        ("exit 134", 134),
        ("ulimit -c 0; kill -ABRT $$", 134),
        ("ulimit -c 0; kill -TRAP $$", 133),
        ("printf trailing; exit 0", 0),
        ("setsid /bin/sleep 60 & exit 0", 0),
        (
            "setsid /bin/sh -c 'setsid /bin/sleep 60 & exit 0' & exit 134",
            134,
        ),
    ] {
        fs::write(
            &helper,
            format!(
                "#!/bin/bash
{script}
"
            ),
        )
        .unwrap();
        fs::set_permissions(&helper, fs::Permissions::from_mode(0o700)).unwrap();
        let r = root();
        let mut c = Command::new(supervisor)
            .args([
                "--owner",
                &std::process::id().to_string(),
                "--root",
                r.to_str().unwrap(),
                "--stop",
                r.join("stop").to_str().unwrap(),
                "--",
                helper.to_str().unwrap(),
                "--lumyte-helper",
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .spawn()
            .unwrap();
        assert_eq!(wait(&mut c), expected, "{script}");
        assert!(!r.exists());
    }
    // Control marker remains independent of a writer blocked on its direct pipe.
    for script in [
        "exec /bin/sleep 60",
        "while :; do printf '%4096s' x; done",
        "exec /bin/cat",
    ] {
        fs::write(
            &helper,
            format!(
                "#!/bin/bash
{script}
"
            ),
        )
        .unwrap();
        let r = root();
        let mut c = Command::new(supervisor)
            .args([
                "--owner",
                &std::process::id().to_string(),
                "--root",
                r.to_str().unwrap(),
                "--stop",
                r.join("stop").to_str().unwrap(),
                "--",
                helper.to_str().unwrap(),
                "--lumyte-helper",
            ])
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .spawn()
            .unwrap();
        thread::sleep(Duration::from_millis(60));
        fs::write(r.join("stop"), []).unwrap();
        assert_eq!(wait(&mut c), 143);
        assert!(!r.exists());
    }
    // Invalid roots and owner identity fail before child launch/root mutation.
    let r = root();
    fs::set_permissions(&r, fs::Permissions::from_mode(0o755)).unwrap();
    let mut c = Command::new(supervisor)
        .args([
            "--owner",
            &std::process::id().to_string(),
            "--root",
            r.to_str().unwrap(),
            "--stop",
            r.join("stop").to_str().unwrap(),
            "--",
            helper.to_str().unwrap(),
            "--lumyte-helper",
        ])
        .spawn()
        .unwrap();
    assert_eq!(wait(&mut c), 2);
    assert!(r.exists());
    fs::remove_dir(&r).unwrap();
    // Closing helper stdout produces immediate EOF even while its owner remains alive.
    fs::write(&helper, "#!/bin/bash\nexec 1>&-\nexec /bin/sleep 60\n").unwrap();
    let r = root();
    let mut c = Command::new(supervisor)
        .args([
            "--owner",
            &std::process::id().to_string(),
            "--root",
            r.to_str().unwrap(),
            "--stop",
            r.join("stop").to_str().unwrap(),
            "--",
            helper.to_str().unwrap(),
            "--lumyte-helper",
        ])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .spawn()
        .unwrap();
    let mut pipe = c.stdout.take().unwrap();
    let (tx, rx) = std::sync::mpsc::channel();
    let reader = thread::spawn(move || {
        use std::io::Read;
        let mut bytes = Vec::new();
        pipe.read_to_end(&mut bytes).unwrap();
        tx.send(bytes).unwrap();
    });
    assert!(rx.recv_timeout(Duration::from_secs(2)).unwrap().is_empty());
    assert!(c.try_wait().unwrap().is_none());
    fs::write(r.join("stop"), []).unwrap();
    assert_eq!(wait(&mut c), 143);
    reader.join().unwrap();
    // A foreign process with the same executable is never a cleanup target.
    let mut foreign = Command::new("/bin/sleep").arg("60").spawn().unwrap();
    fs::write(&helper, "#!/bin/bash\nsetsid /bin/sleep 60 & exit 0\n").unwrap();
    let r = root();
    let mut c = Command::new(supervisor)
        .args([
            "--owner",
            &std::process::id().to_string(),
            "--root",
            r.to_str().unwrap(),
            "--stop",
            r.join("stop").to_str().unwrap(),
            "--",
            helper.to_str().unwrap(),
            "--lumyte-helper",
        ])
        .spawn()
        .unwrap();
    assert_eq!(wait(&mut c), 0);
    assert!(foreign.try_wait().unwrap().is_none());
    foreign.kill().unwrap();
    foreign.wait().unwrap();
    // Parent hard death is observed through its pidfd, independent of protocol IO.
    fs::write(&helper, "#!/bin/bash\nexec /bin/sleep 60\n").unwrap();
    let r = root();
    let owner_script = base.join("owner");
    fs::write(&owner_script, format!("#!/bin/bash\n'{}' --owner $$ --root '{}' --stop '{}/stop' -- '{}' --lumyte-helper &\nsleep .15\nkill -KILL $$\n", supervisor, r.display(), r.display(), helper.display())).unwrap();
    let mut owner = Command::new("/bin/bash")
        .arg(&owner_script)
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .unwrap();
    assert_eq!(owner.wait().unwrap().code(), None);
    let deadline = Instant::now() + Duration::from_secs(10);
    while r.exists() {
        assert!(Instant::now() < deadline);
        thread::sleep(Duration::from_millis(10));
    }
    // Native portal mode preserves fd0, diagnostic fd8 and protocol fd9; root stays outer-owned.
    let r = root();
    fs::create_dir(r.join("portal-runtime")).unwrap();
    let portal = base.join("portal");
    fs::write(
        &portal,
        "#!/bin/bash\nprintf diagnostic >&8\n/bin/cat >&9\n",
    )
    .unwrap();
    let portal_owner = base.join("portal-owner");
    fs::write(&portal_owner, format!("#!/bin/bash\nexec '{}' --portal-session --owner $PPID --root '{}' --stop '{}/isolated-stop' -- '{}' --lumyte-helper 8>&2 9>&1\n", supervisor, r.display(), r.display(), portal.display())).unwrap();
    let mut c = Command::new("/bin/bash")
        .arg(&portal_owner)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    use std::io::Write;
    c.stdin
        .take()
        .unwrap()
        .write_all(b"binary\0protocol")
        .unwrap();
    assert_eq!(wait(&mut c), 0);
    use std::io::Read;
    let mut bytes = Vec::new();
    c.stdout.take().unwrap().read_to_end(&mut bytes).unwrap();
    assert_eq!(bytes, b"binary\0protocol");
    let mut diagnostic = String::new();
    c.stderr
        .take()
        .unwrap()
        .read_to_string(&mut diagnostic)
        .unwrap();
    assert_eq!(diagnostic, "diagnostic");
    assert!(r.exists());
    assert!(!r.join("portal-runtime/bus").exists());
    fs::remove_dir_all(r).unwrap();
    fs::remove_dir_all(base).unwrap();
}

// A portal bus is a sibling dependency, not a disposable orphan while the
// primary is still performing its orderly close after the stop marker.
#[test]
fn cooperative_stop_preserves_bus_until_primary_exits() {
    let supervisor = env!("CARGO_BIN_EXE_lumyte-cef-supervisor");
    let base = std::env::temp_dir().join(format!("supervisor-dependency-{}", std::process::id()));
    fs::create_dir(&base).unwrap();
    let r = PathBuf::from(format!("/tmp/lumyte-browser-grace-{}", std::process::id()));
    fs::create_dir(&r).unwrap();
    fs::set_permissions(&r, fs::Permissions::from_mode(0o700)).unwrap();
    fs::create_dir(r.join("portal-runtime")).unwrap();
    let bus = base.join("dbus-daemon");
    fs::write(
        &bus,
        r#"#!/bin/bash
trap 'printf dead > "$TMPDIR/bus-dead"; exit 0' TERM
: > "$TMPDIR/portal-runtime/bus"
while :; do /bin/sleep .01; done
"#,
    )
    .unwrap();
    let helper = base.join("helper");
    fs::write(
        &helper,
        r#"#!/bin/bash
trap '' TERM
: > "$TMPDIR/ready"
while [[ ! -e "$TMPDIR/isolated-stop" ]]; do /bin/sleep .01; done
/bin/sleep .15
[[ ! -e "$TMPDIR/bus-dead" ]] || exit 42
exit 0
"#,
    )
    .unwrap();
    for file in [&bus, &helper] {
        fs::set_permissions(file, fs::Permissions::from_mode(0o700)).unwrap();
    }
    let mut child = Command::new(supervisor)
        .args([
            "--portal-session",
            "--owner",
            &std::process::id().to_string(),
            "--root",
            r.to_str().unwrap(),
            "--stop",
            r.join("isolated-stop").to_str().unwrap(),
            "--",
            helper.to_str().unwrap(),
            "--lumyte-helper",
        ])
        .env("TMPDIR", &r)
        .env("PATH", format!("{}:/usr/bin:/bin", base.display()))
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .unwrap();
    let deadline = Instant::now() + Duration::from_secs(3);
    while !r.join("ready").exists() {
        assert!(Instant::now() < deadline, "primary startup handshake");
        thread::sleep(Duration::from_millis(10));
    }
    fs::write(r.join("isolated-stop"), []).unwrap();
    let code = wait(&mut child);
    fs::remove_dir_all(&r).unwrap();
    fs::remove_dir_all(&base).unwrap();
    assert_eq!(
        code, 0,
        "bus must remain alive until the primary completes its own shutdown"
    );
}
