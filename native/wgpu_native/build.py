#!/usr/bin/env python3
"""Build the ABI-compatible wgpu-native command-buffer ownership correction."""
import argparse
import hashlib
import io
import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request

import clang_runtime

COMMIT = "d2e3330ade4ae1bb238d76b485926f067e7ee64c"
ARCHIVE_SHA = "78b8cb2e756f8ef4e2bc5d127c5f1cf345915ceb81830c5382f807adaaab3394"
HEADER_COMMIT = "7d3186c3dd2c708703524027b46b8703534ab3cc"
HEADER_SHA = "4ab5756e314c274c6c889c662c100e897dba4bfb2673f6ec5b104d47361100e6"
ORIGINAL_SHA = "4e50104dab8686ea0ebae0c11bb6eccfe1a676f1aa9bf57cb802c4988a52865f"
PATCHED_SHA = "007fc7021336b8854f450c2ea9c90a16d1db58e1aaaebec7a228d1d6be2ef467"


def fetch(url, path, expected):
    if not path.exists():
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read()
        if hashlib.sha256(data).hexdigest() != expected:
            raise RuntimeError("Downloaded source SHA-256 mismatch: " + url)
        path.write_bytes(data)
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != expected:
        raise RuntimeError("Cached source SHA-256 mismatch: " + str(path))
    return data


def host_rid():
    operating_system = {"Linux": "linux", "Darwin": "osx", "Windows": "win"}.get(platform.system())
    architecture = {"x86_64": "x64", "AMD64": "x64", "aarch64": "arm64", "arm64": "arm64", "ARM64": "arm64"}.get(platform.machine())
    if not operating_system or not architecture:
        raise RuntimeError("Unsupported wgpu-native host platform")
    return operating_system + "-" + architecture


def verify(binary, provenance, rid):
    proof = json.loads(provenance.read_text())
    required = {"upstreamCommit": COMMIT, "archiveSha256": ARCHIVE_SHA,
                "originalSourceSha256": ORIGINAL_SHA, "patchedSourceSha256": PATCHED_SHA,
                "hostRid": rid}
    for key, expected in required.items():
        if proof.get(key) != expected:
            raise RuntimeError("Native artifact provenance mismatch: " + key)
    if hashlib.sha256(binary.read_bytes()).hexdigest() != proof.get("binarySha256"):
        raise RuntimeError("Native artifact binary SHA-256 mismatch")
    print("Verified corrected native artifact: " + str(binary) + " (" + rid + ")")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--binary", type=Path)
    parser.add_argument("--provenance", type=Path)
    parser.add_argument("--rid")
    parser.add_argument("--cargo", default="cargo")
    args = parser.parse_args()
    if args.verify:
        verify(args.binary, args.provenance, args.rid)
        return
    if args.output is None:
        parser.error("--output is required for source builds")
    metadata_path = Path(__file__).with_name("source-info.json")
    source_info = json.loads(metadata_path.read_text())
    if source_info["archiveSha256"] != ARCHIVE_SHA or source_info["headerSha256"] != HEADER_SHA:
        raise RuntimeError("Pinned source metadata disagrees with build script")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    archive = fetch("https://codeload.github.com/gfx-rs/wgpu-native/tar.gz/" + COMMIT,
                    output / "source.tar.gz", ARCHIVE_SHA)
    source = output / ("wgpu-native-" + COMMIT)
    if not source.exists():
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
            package.extractall(output, filter="data")
    header = fetch("https://raw.githubusercontent.com/webgpu-native/webgpu-headers/" + HEADER_COMMIT + "/webgpu.h",
                   output / "webgpu.h", HEADER_SHA)
    (source / "ffi/webgpu-headers/webgpu.h").write_bytes(header)
    if hashlib.sha256((source / "Cargo.lock").read_bytes()).hexdigest() != source_info["cargoLockSha256"]:
        raise RuntimeError("Upstream Cargo.lock SHA-256 mismatch")
    lib = source / "src/lib.rs"
    data = lib.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest == ORIGINAL_SHA:
        old = b"id: command_buffer_id,\n        open: atomic::AtomicBool::new(false),"
        new = b"id: command_buffer_id,\n        open: atomic::AtomicBool::new(true),"
        if data.count(old) != 1:
            raise RuntimeError("Native patch context is not unique")
        lib.write_bytes(data.replace(old, new))
    if hashlib.sha256(lib.read_bytes()).hexdigest() != PATCHED_SHA:
        raise RuntimeError("Patched native source SHA-256 mismatch")
    environment = os.environ.copy()
    environment["RUSTUP_TOOLCHAIN"] = "1.99.1"
    environment["RUSTUP_AUTO_INSTALL"] = "0"
    cargo_version = subprocess.check_output([args.cargo, "--version"], env=environment, text=True).strip()
    rust_version = subprocess.check_output([environment.get("RUSTC", "rustc"), "--version"], env=environment, text=True).strip()
    if not cargo_version.startswith("cargo 1.99.1 ") or not rust_version.startswith("rustc 1.99.1 "):
        raise RuntimeError("Use the existing pinned Rust/Cargo 1.99.1 toolchain; this build never installs SDKs")
    clang_runtime.prepare(output, host_rid(), source, environment)
    subprocess.run([args.cargo, "build", "--locked", "--release", "--manifest-path", str(source / "Cargo.toml"),
                    "--target-dir", str(output / "target")], check=True, env=environment)
    if os.name == "nt":
        original, renamed = "wgpu_native.dll", "lumyte_wgpu_native.dll"
    elif os.uname().sysname == "Darwin":
        original, renamed = "libwgpu_native.dylib", "liblumyte_wgpu_native.dylib"
    else:
        original, renamed = "libwgpu_native.so", "liblumyte_wgpu_native.so"
    shutil.copy2(output / "target/release" / original, output / renamed)
    metadata = json.loads(subprocess.check_output(
        [args.cargo, "metadata", "--locked", "--format-version", "1", "--manifest-path", str(source / "Cargo.toml")],
        env=environment, text=True))
    header_license = Path(__file__).with_name("LICENSE.webgpu-headers").read_bytes()
    if hashlib.sha256(header_license).hexdigest() != "17420d366df90c474bd70ad474694956cdb7fc64be70387a49a45458c4152d22":
        raise RuntimeError("Pinned WebGPU header license SHA-256 mismatch")
    notices = ["wgpu-native and locked Rust dependency license notices\n",
               "WebGPU-native headers " + HEADER_COMMIT + "\n" + header_license.decode()]
    for package in sorted(metadata["packages"], key=lambda package: (package["name"], package["version"])):
        directory = Path(package["manifest_path"]).parent
        notices.append("\n## " + package["name"] + " " + package["version"] + " (" + str(package.get("license")) + ")\n")
        files = set()
        for pattern in ("LICENSE*", "LICENCE*", "COPYING*", "NOTICE*"):
            files.update(path for path in directory.glob(pattern) if path.is_file())
        if package.get("license_file"):
            files.add(directory / package["license_file"])
        for path in sorted(files):
            notices.append("\n### " + path.name + "\n" + path.read_text(encoding="utf-8", errors="replace"))
    (output / "WGPU-THIRD-PARTY-NOTICES.txt").write_text("\n".join(notices), encoding="utf-8")
    sysroot = Path(subprocess.check_output([environment.get("RUSTC", "rustc"), "--print", "sysroot"], env=environment, text=True).strip())
    shutil.copy2(sysroot / "share/doc/rust/COPYRIGHT-library.html", output / "RUST-STANDARD-LIBRARY-NOTICES.html")
    proof = {"upstreamCommit": COMMIT, "archiveSha256": ARCHIVE_SHA,
             "originalSourceSha256": ORIGINAL_SHA, "patchedSourceSha256": PATCHED_SHA,
             "binarySha256": hashlib.sha256((output / renamed).read_bytes()).hexdigest(),
             "cargo": cargo_version, "rustc": rust_version, "hostRid": host_rid()}
    (output / "provenance.json").write_text(json.dumps(proof, indent=2) + "\n")


if __name__ == "__main__":
    main()
