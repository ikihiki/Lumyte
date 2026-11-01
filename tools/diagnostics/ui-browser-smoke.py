"""Run a real Browser HTTP Agent against a separate diagnostic service and typed Operator."""
import argparse
import http.client
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--node', default='node')
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
output = root / 'artifacts/ui-diagnostics-browser'
output.mkdir(parents=True, exist_ok=True)

def port():
    with socket.socket() as connection:
        connection.bind(('127.0.0.1', 0))
        return connection.getsockname()[1]

hp, gp = port(), port()
while gp == hp:
    gp = port()
env = dict(os.environ, LUMYTE_DIAGNOSTICS_GAME_TOKEN=secrets.token_hex(32),
           LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN=secrets.token_hex(32), LUMYTE_DIAGNOSTICS_HTTP_PORT=str(hp),
           LUMYTE_DIAGNOSTICS_GRPC_PORT=str(gp), LUMYTE_UI_INPUT_ONLY='1', LUMYTE_UI_DIAGNOSTICS='1',
           LUMYTE_DIAGNOSTIC_SERVICE=f'http://127.0.0.1:{hp}/', DOTNET=args.dotnet)
# The browser verifier chooses a separate ephemeral static-server origin. Read it via a callback file.
origin = output / 'origin.txt'
origin.unlink(missing_ok=True)
env['LUMYTE_UI_ORIGIN_FILE'] = str(origin)
# Start Chromium first; .NET startup retries the first connection after the server origin is known.
with (output / 'browser.log').open('w') as browserlog, (output / 'server.log').open('w') as serverlog:
    browser = subprocess.Popen([args.node, 'samples/Lumyte.UI.Browser.Sample/verify-browser.mjs'], cwd=root, env=env, stdout=browserlog, stderr=subprocess.STDOUT)
    server = None
    try:
        for _ in range(200):
            if origin.exists():
                break
            if browser.poll() is not None:
                raise RuntimeError('Browser verifier failed before publishing its origin')
            time.sleep(.01)
        env['LUMYTE_DIAGNOSTICS_ALLOWED_ORIGINS'] = origin.read_text().strip()
        server = subprocess.Popen([args.dotnet, 'src/Diagnostics/Lumyte.Diagnostics.Server/bin/Release/net10.0/Lumyte.Diagnostics.Server.dll'], cwd=root, env=env, stdout=serverlog, stderr=subprocess.STDOUT)
        browser.wait(timeout=90)
        assert browser.returncode == 0, (output / 'browser.log').read_text()
        print('Browser HTTP Agent + external typed Operator passed: discovery, control, Click, Expect, Enter, count and cleanup.')
    finally:
        if browser.poll() is None:
            browser.terminate()
            browser.wait(timeout=10)
        if server is not None:
            server.terminate()
            server.wait(timeout=10)
