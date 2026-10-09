import argparse
import http.client
import json
import os
import secrets
import shutil
import socket
import subprocess
import time
import uuid
from pathlib import Path

parser = argparse.ArgumentParser(
    description="Verify independent server/game processes over HTTP and MagicOnion."
)
parser.add_argument("--dotnet", default="dotnet")
parser.add_argument("--output", default="artifacts/diagnostics-communication/processes")
args = parser.parse_args()
dotnet = shutil.which(args.dotnet)
if not dotnet:
    raise SystemExit("dotnet was not found; activate the repository SDK first")
root = Path(__file__).resolve().parents[2]
work = (root / args.output).resolve()
work.mkdir(parents=True, exist_ok=True)


def port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


hp, gp = port(), port()
while gp == hp:
    gp = port()
env = os.environ.copy()
env.update(
    LUMYTE_DIAGNOSTICS_GAME_TOKEN=secrets.token_hex(32),
    LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN=secrets.token_hex(32),
    LUMYTE_DIAGNOSTICS_HTTP_PORT=str(hp),
    LUMYTE_DIAGNOSTICS_GRPC_PORT=str(gp),
)


def request(method, path, data=None):
    c = http.client.HTTPConnection("127.0.0.1", hp, timeout=10)
    b = None if data is None else json.dumps(data).encode()
    h = {"Authorization": "Bearer " + env["LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN"]}
    if b is not None:
        h["Content-Type"] = "application/json"
    c.request(method, path, b, h)
    r = c.getresponse()
    raw = r.read()
    status = r.status
    c.close()
    assert status in [200, 204], (status, raw[:200])
    return json.loads(raw) if raw else None


serverlog = (work / "remote-server-process.log").open("w")
p = subprocess.Popen(
    [
        dotnet,
        "run",
        "--project",
        "src/Diagnostics/Lumyte.Diagnostics.Server",
        "-c",
        "Release",
        "--no-build",
    ],
    cwd=root,
    env=env,
    stdout=serverlog,
    stderr=subprocess.STDOUT,
)
results = []
try:
    for i in range(200):
        try:
            request("GET", "/diagnostics/v1/sessions")
            break
        except (OSError, AssertionError):
            time.sleep(0.05)
    else:
        raise RuntimeError("Server did not start")
    for transport, endpoint in [("http", hp), ("magiconion", gp)]:
        logfile = work / ("remote-" + transport + "-process.log")
        log = logfile.open("w")
        game = subprocess.Popen(
            [
                dotnet,
                "run",
                "--project",
                "samples/Lumyte.Diagnostics.Remote.Sample",
                "-c",
                "Release",
                "--no-build",
                "--",
                transport,
                "http://127.0.0.1:" + str(endpoint),
                "30",
            ],
            cwd=root,
            env=env,
            stdout=log,
            stderr=subprocess.STDOUT,
        )
        try:
            for i in range(200):
                sessions = request("GET", "/diagnostics/v1/sessions")
                if sessions:
                    break
                if game.poll() is not None:
                    raise RuntimeError("Game exited before connection")
                time.sleep(0.05)
            assert len(sessions) == 1, sessions
            session = sessions[0]
            sid = session["sessionId"]
            assert session["catalog"][0]["operations"][0]["id"] == "override-button"

            operation_started = time.monotonic()
            result = request(
                "POST",
                f"/diagnostics/v1/sessions/{sid}/operations",
                {
                    "requestId": str(uuid.uuid4()),
                    "subsystemId": "input",
                    "operationId": "override-button",
                    "arguments": {
                        "button": {"kind": 3, "string": "Jump"},
                        "pressed": {"kind": 0, "boolean": True},
                        "duration-ms": {"kind": 1, "int64": "5000"},
                    },
                    "timeoutMilliseconds": 3000,
                },
            )
            operation_ms = (time.monotonic() - operation_started) * 1000
            assert result["status"] == "success", result
            for i in range(100):
                if "Input state: Jump=True" in logfile.read_text():
                    break
                time.sleep(0.01)
            else:
                raise AssertionError("Remote operation did not change game input")
            for i in range(100):
                events = request("GET", f"/diagnostics/v1/sessions/{sid}/telemetry")
                if {e["kind"] for e in events} >= {"metric", "log", "span"}:
                    break
                time.sleep(0.02)
            metric = next(e for e in events if e["kind"] == "metric")
            assert metric["value"]["int64"] == "9007199254740993", metric
            span = next(e for e in events if e["kind"] == "span")
            eventlog = next(e for e in events if e["kind"] == "log")
            assert span["traceId"] == eventlog["traceId"]
            request("DELETE", f"/diagnostics/v1/sessions/{sid}/connection")
            game.wait(timeout=10)
            log.flush()
            assert game.returncode == 0, logfile.read_text()
            assert "Disconnected: Jump=False" in logfile.read_text()
            record = {
                "transport": transport,
                "serverProcessId": p.pid,
                "gameProcessId": game.pid,
                "catalogOperation": "override-button",
                "result": "success",
                "operationElapsedMilliseconds": round(operation_ms, 3),
                "inputChanged": True,
                "metricInt64": metric["value"]["int64"],
                "traceLogCorrelated": True,
                "disconnectReleasedInput": True,
            }
            results.append(record)
            print(json.dumps(record), flush=True)
        finally:
            if game.poll() is None:
                game.terminate()
                game.wait(timeout=10)
            log.close()
finally:
    p.terminate()
    try:
        p.wait(timeout=10)
    except subprocess.TimeoutExpired:
        p.kill()
        p.wait()
    serverlog.close()
(work / "communication-smoke-results.json").write_text(
    json.dumps(results, indent=2) + "\n"
)
