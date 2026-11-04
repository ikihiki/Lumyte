#!/usr/bin/env python3
"""Check built extension CLIs without permitting GPU initialization.

Run after Release builds: python3 tools/benchmarks/graphics-scenarios/check-extension-cli.py
This is execution verification, not a performance test.
"""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
ASSEMBLIES = {
    family: ROOT / f"benchmarks/Graphics/Lumyte.Graphics.{family}.Benchmarks/bin/Release/net10.0" / name
    for family, name in {
        "Text": "Lumyte.Graphics.Text.Benchmarks.dll",
        "Effects": "Lumyte.Graphics.Effects.Benchmarks.dll",
        "Primitives": "Lumyte.Graphics.Primitives.Benchmarks.dll",
        "Ui": "Lumyte.Graphics.UiScenarios.dll",
    }.items()
}


def main():
    for assembly in ASSEMBLIES.values():
        if not assembly.is_file():
            raise SystemExit(f"Build Release first: {assembly}")
    env = dict(os.environ, VK_DRIVER_FILES="/nonexistent/cli-test-no-gpu.json")
    cases = []
    for family, mode in [("Text", "--smoke"), ("Effects", "--smoke"), ("Ui", "--gpu")]:
        cases.extend([
            (family, [mode, "--unknown"], "Unknown option"),
            (family, [mode, mode], "Duplicate option"),
            (family, [mode, "--frames"], "Missing value"),
            (family, [mode, "--list"], "exactly one"),
            (family, ["--list", "--frames", "1"], "not supported"),
        ])
    cases.extend([
        ("Text", ["--cpu", "--cpu"], "exactly once"),
        ("Text", ["--cpu", "--smoke"], "without another"),
        ("Text", ["--smoke", "--depth", "3"], "not supported"),
        ("Text", ["--run", "--case", "fixed", "--method", "vector", "--source", "check", "--depth", "2"], "Depth"),
        ("Text", ["--run", "--case", "fixed", "--method", "bad", "--source", "check"], "Choose vector"),
        ("Effects", ["--smoke", "--frames", "4"], "not supported"),
        ("Effects", ["--run", "--source", "check", "--profile", "bad"], "matching"),
        ("Ui", ["--gpu", "--case"], "Missing value"),
        ("Ui", ["--gpu", "--frames", "0"], "frames"),
        ("Ui", ["--gpu", "--case", "bad"], "No selected"),
        ("Primitives", ["--unknown", "1"], "Unknown option"),
        ("Primitives", ["--frames"], "Missing value"),
        ("Primitives", ["--frames", "1", "--frames", "2"], "Duplicate option"),
        ("Primitives", ["--case", "bad"], "Choose --case"),
        ("Primitives", ["--frames", "0"], "Frames"),
        ("Primitives", ["--warmup", "2001"], "Frames"),
    ])
    with tempfile.TemporaryDirectory(prefix="lumyte-cli-check-") as temporary:
        output = Path(temporary) / "must-not-be-created"
        cases.append(("Primitives", ["--case", "bad", "--output", str(output)], "Choose --case"))
        for family, args, expected in cases:
            result = subprocess.run(["dotnet", str(ASSEMBLIES[family]), *args], cwd=ROOT,
                                    env=env, capture_output=True, text=True, timeout=30)
            message = result.stdout + result.stderr
            assert result.returncode != 0, (family, args, "accepted invalid input")
            assert expected.lower() in message.lower(), (family, args, message)
            assert not any(marker in message for marker in ["VulkanDevice.Create", "vkCreateInstance", "ERROR_INCOMPATIBLE_DRIVER"]), (family, args, message)
        assert not output.exists(), "Invalid primitive options created output"
    for family, args, count in [("Text", ["--list"], 7), ("Effects", ["--list"], 9), ("Ui", ["--list"], 14)]:
        result = subprocess.run(["dotnet", str(ASSEMBLIES[family]), *args], cwd=ROOT,
                                env=env, capture_output=True, text=True, timeout=30, check=True)
        assert len([json.loads(line) for line in result.stdout.splitlines()]) == count
    result = subprocess.run(["dotnet", str(ASSEMBLIES["Text"]), "--cpu", "--list", "flat"],
                            cwd=ROOT, env=env, capture_output=True, text=True, timeout=30, check=True)
    assert "TextBenchmarks" in result.stdout and "DistanceFieldGenerationBenchmarks" in result.stdout
    print(json.dumps({"status": "passed", "invalidInvocations": len(cases), "validCatalogs": 3, "bdnPassthrough": True, "gpuInitialization": False}))


if __name__ == "__main__":
    main()
