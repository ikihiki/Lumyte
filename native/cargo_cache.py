#!/usr/bin/env python3
"""Standard Cargo target-cache identity; never a staged-library build bypass."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

TARGETS = ("artifacts/wgpu-native/target", "artifacts/text-parley/native/cargo",
           "artifacts/svg-usvg/native/cargo")
ENVIRONMENT = ("RUSTFLAGS", "CARGO_ENCODED_RUSTFLAGS", "RUSTDOCFLAGS", "RUSTC",
               "RUSTC_WRAPPER", "RUSTC_WORKSPACE_WRAPPER", "CC", "CXX", "AR",
               "CFLAGS", "CXXFLAGS", "CPPFLAGS", "LDFLAGS", "LIBCLANG_PATH",
               "BINDGEN_EXTRA_CLANG_ARGS", "INCLUDE", "LIB", "LIBPATH",
               "WindowsSDKVersion", "VCToolsVersion", "SDKROOT", "MACOSX_DEPLOYMENT_TARGET", "CPATH", "C_INCLUDE_PATH",
               "CPLUS_INCLUDE_PATH", "LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX")


def digest(path):
    hasher = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(block)
    return hasher.hexdigest()


def source_closure(root):
    names = subprocess.check_output([
        "git", "ls-files", "-z", "--cached", "--others", "--exclude-standard",
        "--", "native", "build", "tools/setup", ".cargo", ".github/actions/cache-native",
        "mise.toml", "mise.lock", "global.json", "Directory.Build.props",
        "src/**/*.h", "src/**/*.hpp", "src/**/*.targets", "src/**/*.props", "src/**/*.csproj"], cwd=root).split(b"\0")
    entries = []
    for raw in sorted(set(names) - {b""}):
        name = os.fsdecode(raw)
        path = root / name
        if not path.is_file():
            raise RuntimeError("Missing native cache source input: " + name)
        entries.append([name, digest(path)])
    if not entries:
        raise RuntimeError("No native cache source inputs")
    return entries


def build_environment(environment):
    # Stable compile inputs only: no tokens, CI job identifiers or timestamps.
    return sorted((key, value) for key, value in environment.items()
                  if key != "LUMYTE_NATIVE_CACHE_KEY" and (key in ENVIRONMENT or key.startswith(("CARGO_TARGET_", "CARGO_PROFILE_", "CC_", "CXX_", "AR_", "CFLAGS_", "CXXFLAGS_", "BINDGEN_EXTRA_CLANG_ARGS_", "LUMYTE_", "Lumyte"))))


def output(arguments, environment):
    return subprocess.check_output(arguments, env=environment, encoding="utf-8", errors="replace", stderr=subprocess.STDOUT).strip()


def tool_identity(command, arguments, environment):
    path = shutil.which(command, path=environment.get("PATH"))
    if not path:
        raise RuntimeError("Native cache identity tool not found: " + command)
    return [str(Path(path).resolve()), digest(Path(path)), output([path, *arguments], environment)]


def tool_identities(root, environment):
    suffix = ".exe" if os.name == "nt" else ""
    cargo_home = Path(environment.get("CARGO_HOME", root / "artifacts/build/tools/cargo"))
    rustup = cargo_home / "bin" / ("rustup" + suffix)
    if not rustup.exists():
        candidate = shutil.which("rustup", path=environment.get("PATH"))
        if not candidate:
            raise RuntimeError("Run Setup --setup-tool parley before cache restore")
        rustup = Path(candidate)
    tools = {}
    for name in ("cargo", "rustc"):
        actual = Path(output([str(rustup), "which", "--toolchain", "1.99.1", name], environment))
        tools[name] = [str(actual), digest(actual), output([str(actual), "-Vv"], environment)]
    sysroot = Path(output([str(rustup), "run", "1.99.1", "rustc", "--print", "sysroot"], environment))
    # rustc is a thin executable; include its dynamically loaded compiler too.
    compiler_libraries = list((sysroot / "lib").glob("*rustc_driver*")) + list((sysroot / "bin").glob("*rustc_driver*"))
    tools["rustc-driver"] = sorted([path.name, digest(path)] for path in compiler_libraries)
    # Standard libraries and LLVM can change independently of the thin driver.
    rust_libraries = [path for path in (sysroot / "lib").rglob("*")
                      if path.is_file() and path.suffix in (".rlib", ".so", ".dylib", ".dll", ".a", ".lib")]
    tools["rust-libraries"] = sorted([str(path.relative_to(sysroot)), digest(path)] for path in rust_libraries)
    if platform.system() == "Linux":
        tools["cc"] = tool_identity(environment.get("CC", "cc"), ["--version"], environment)
        tools["ld"] = tool_identity("ld", ["--version"], environment)
        tools["packages"] = output(["dpkg-query", "-W", "-f=" + chr(36) + "{Package}=" + chr(36) + "{Version}\n"], environment)
    elif platform.system() == "Windows":
        # Compiler/SDK initialized by activate.ps1, exactly as in Compile.
        # cl without sources returns failure; hash the executable and use SDK
        # versions/INCLUDE/LIB plus runner image identity instead.
        compiler = shutil.which("cl", path=environment.get("PATH"))
        if not compiler:
            raise RuntimeError("Activate the VS compiler before cache restore")
        tools["cl"] = [compiler, digest(Path(compiler))]
        tools["link"] = tool_identity("link", ["/?"], environment)
    else:
        raise RuntimeError("Native CI cache supports Linux and Windows")
    return tools


def identity(root, environment, tools):
    proof = {"schema": 2, "kind": "standard-cargo-target",
             "host": [platform.system(), platform.machine(), environment.get("ImageOS", ""), environment.get("ImageVersion", "")],
             "source": source_closure(root), "environment": build_environment(environment), "tools": tools}
    encoded = json.dumps(proof, sort_keys=True, separators=(",", ":")).encode()
    return "native-cargo-v2-" + platform.system() + "-" + platform.machine() + "-" + hashlib.sha256(encoded).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    environment = os.environ.copy()
    key = identity(root, environment, tool_identities(root, environment))
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as stream:
            stream.write("key=" + key + "\n")
    print(json.dumps({"key": key, "paths": TARGETS}))


if __name__ == "__main__":
    main()
