"""Private checksum-pinned ClangSharp libclang; host C SDK remains explicit.

Only public Clang C API is used for the fail-early header parse probe.
"""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shlex
import subprocess
import urllib.request
import uuid
import zipfile

MAXIMUM_ARCHIVE = 100 * 1024 * 1024


def resolve_library(root, rid):
    override = os.environ.get("LIBCLANG_PATH")
    if override:
        return Path(override).resolve()
    metadata = json.loads(Path(__file__).with_name("clang-runtimes.json").read_text())
    if rid not in metadata:
        raise RuntimeError("No private libclang pin for " + rid + "; supply LIBCLANG_PATH")
    spec = metadata[rid]
    directory = root / "tools" / "libclang" / rid / spec["version"]
    directory.mkdir(parents=True, exist_ok=True)
    archive = directory / "runtime.nupkg"
    if not archive.exists():
        package = "libclang.runtime." + rid
        url = "https://api.nuget.org/v3-flatcontainer/" + package + "/" + spec["version"] + "/" + package + "." + spec["version"] + ".nupkg"
        with urllib.request.urlopen(url, timeout=90) as response:
            data = response.read(MAXIMUM_ARCHIVE + 1)
        if len(data) > MAXIMUM_ARCHIVE:
            raise RuntimeError("libclang package exceeds download limit")
        verify_digest(data, spec["sha256"])
        temporary = archive.with_suffix("." + uuid.uuid4().hex + ".tmp")
        temporary.write_bytes(data)
        temporary.replace(archive)
    data = archive.read_bytes()
    verify_digest(data, spec["sha256"])
    # Extract only known members; re-check cached binary against verified archive.
    filename = "libclang.dll" if rid.startswith("win-") else "libclang.dylib" if rid.startswith("osx-") else "libclang.so"
    with zipfile.ZipFile(archive) as package:
        for member, target in [("runtimes/" + rid + "/native/" + filename, directory / filename), ("LICENSE.TXT", directory / "LICENSE.TXT")]:
            if package.getinfo(member).file_size > 512 * 1024 * 1024:
                raise RuntimeError("libclang member exceeds extraction limit")
            expected = package.read(member)
            if not target.exists() or target.read_bytes() != expected:
                target.write_bytes(expected)
    return directory


def verify_digest(data, expected):
    if hashlib.sha256(data).hexdigest() != expected:
        raise RuntimeError("libclang package SHA-256 mismatch")


def host_header_args(rid):
    if rid.startswith("linux-"):
        # GCC's standard headers are needed even when no clang executable exists.
        compiler = os.environ.get("CC", "cc")
        include = subprocess.check_output([compiler, "-print-file-name=include"], text=True).strip()
        if not (Path(include) / "stddef.h").exists():
            raise RuntimeError("Host C SDK unavailable: install GCC and libc development headers")
        return ["-isystem", include]
    if rid.startswith("osx-"):
        sdk = subprocess.check_output(["xcrun", "--sdk", "macosx", "--show-sdk-path"], text=True).strip()
        return ["-isysroot", sdk]
    # VS developer environment may be supplied explicitly; otherwise locate SDK.
    includes = [p for p in os.environ.get("INCLUDE", "").split(os.pathsep) if p]
    if not includes:
        vswhere = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
        component = "Microsoft.VisualStudio.Component.VC.Tools.ARM64" if rid == "win-arm64" else "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"
        installation = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires", component, "-property", "installationPath"], text=True).strip()
        if not installation:
            raise RuntimeError("Visual Studio C++ host tools were not found")
        msvc = sorted((Path(installation) / "VC/Tools/MSVC").glob("*/include"))
        kits = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Windows Kits/10/Include"
        ucrt = sorted(kits.glob("*/ucrt"))
        if not msvc or not ucrt:
            raise RuntimeError("Host C SDK unavailable: VS C++ tools and Windows 10/11 SDK UCRT headers required")
        includes = [str(msvc[-1]), str(ucrt[-1])]
    return [arg for path in includes for arg in ["-isystem", path]]


def probe_headers(directory, header, args):
    names = ["libclang.dll", "libclang.so", "libclang.dylib"]
    library = next((directory / name for name in names if (directory / name).exists()), None)
    if library is None:
        raise RuntimeError("LIBCLANG_PATH must contain a libclang shared library")
    clang = ctypes.CDLL(str(library))
    clang.clang_createIndex.restype = ctypes.c_void_p
    clang.clang_parseTranslationUnit.restype = ctypes.c_void_p
    clang.clang_parseTranslationUnit.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.POINTER(ctypes.c_char_p), ctypes.c_int, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint]
    clang.clang_getNumDiagnostics.argtypes = [ctypes.c_void_p]
    clang.clang_getDiagnostic.restype = ctypes.c_void_p
    clang.clang_getDiagnostic.argtypes = [ctypes.c_void_p, ctypes.c_uint]
    clang.clang_getDiagnosticSeverity.argtypes = [ctypes.c_void_p]
    clang.clang_disposeDiagnostic.argtypes = [ctypes.c_void_p]
    clang.clang_disposeTranslationUnit.argtypes = [ctypes.c_void_p]
    clang.clang_disposeIndex.argtypes = [ctypes.c_void_p]
    index = clang.clang_createIndex(0, 1)
    encoded = (ctypes.c_char_p * len(args))(*(arg.encode() for arg in args))
    unit = clang.clang_parseTranslationUnit(index, str(header).encode(), encoded, len(args), None, 0, 0)
    try:
        if not unit:
            raise RuntimeError("libclang cannot parse pinned WebGPU headers")
        for number in range(clang.clang_getNumDiagnostics(unit)):
            diagnostic = clang.clang_getDiagnostic(unit, number)
            severity = clang.clang_getDiagnosticSeverity(diagnostic)
            clang.clang_disposeDiagnostic(diagnostic)
            if severity >= 3:
                raise RuntimeError("Host C SDK header parse failed; see clang diagnostic above")
    finally:
        if unit:
            clang.clang_disposeTranslationUnit(unit)
        clang.clang_disposeIndex(index)
    print("Verified libclang + host C SDK against pinned WebGPU headers: " + str(library), flush=True)


def prepare(root, rid, source, environment):
    directory = resolve_library(root, rid)
    targets = {"linux-x64": "x86_64-unknown-linux-gnu", "linux-arm64": "aarch64-unknown-linux-gnu",
               "win-x64": "x86_64-pc-windows-msvc", "win-arm64": "aarch64-pc-windows-msvc",
               "osx-x64": "x86_64-apple-darwin", "osx-arm64": "aarch64-apple-darwin"}
    args = ["--target=" + targets[rid], *host_header_args(rid)]
    # shlex matches bindgen's parsing on every host (including Windows paths).
    previous = environment.get("BINDGEN_EXTRA_CLANG_ARGS", "")
    args += shlex.split(previous)
    probe_headers(directory, source / "ffi/wgpu.h", ["-I" + str(source / "ffi/webgpu-headers"), *args])
    environment["LIBCLANG_PATH"] = str(directory)
    environment["BINDGEN_EXTRA_CLANG_ARGS"] = shlex.join(args)
