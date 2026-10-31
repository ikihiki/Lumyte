"""Verify server, game Agent and typed Operator as separate processes (no UI mocks)."""
import argparse
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import struct
import subprocess
import time
import zlib

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--gpu', choices=['none', 'vulkan', 'wgpu'], default='none')
parser.add_argument('--output', default='artifacts/ui-diagnostics-processes')
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
output = (root / args.output).resolve()
output.mkdir(parents=True, exist_ok=True)

def port():
    with socket.socket() as connection:
        connection.bind(('127.0.0.1', 0))
        return connection.getsockname()[1]

hp, gp = port(), port()
while hp == gp:
    gp = port()
env = dict(os.environ, LUMYTE_DIAGNOSTICS_GAME_TOKEN=secrets.token_hex(32),
           LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN=secrets.token_hex(32),
           LUMYTE_DIAGNOSTICS_HTTP_PORT=str(hp), LUMYTE_DIAGNOSTICS_GRPC_PORT=str(gp),
           LUMYTE_UI_CLOSE_SAMPLE='1')

def request():
    connection = http.client.HTTPConnection('127.0.0.1', hp, timeout=2)
    try:
        connection.request('GET', '/diagnostics/v1/sessions', headers={'Authorization': 'Bearer ' + env['LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN']})
        response = connection.getresponse()
        assert response.status == 200
        return json.loads(response.read())
    finally:
        connection.close()

def dll(project):
    return str(root / project / 'bin/Release/net10.0' / (Path(project).name + '.dll'))

def validate_png(path):
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    offset = 8
    compressed = b''
    while offset < len(data):
        length = struct.unpack('>I', data[offset:offset+4])[0]
        kind, body = data[offset+4:offset+8], data[offset+8:offset+8+length]
        assert zlib.crc32(kind + body) == struct.unpack('>I', data[offset+8+length:offset+12+length])[0]
        if kind == b'IHDR':
            assert struct.unpack('>II', body[:8]) == (240, 160)
        if kind == b'IDAT':
            compressed += body
        offset += length + 12
    raw = zlib.decompress(compressed)
    pixels = b''.join(raw[row*961+1:(row+1)*961] for row in range(160))
    assert sum(pixels[index:index+4] == b'\x00\xff\x00\xff' for index in range(0, len(pixels), 4)) == 64

operator_directory = root / 'samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample/bin/Release/net10.0'
assert not (operator_directory / 'Lumyte.UI.dll').exists()
assert not any(operator_directory.glob('Lumyte.Graphics.*.dll'))
results = []
with (output / 'server.log').open('w') as serverlog:
    server = subprocess.Popen([args.dotnet, dll('src/Diagnostics/Lumyte.Diagnostics.Server')], cwd=root, env=env, stdout=serverlog, stderr=subprocess.STDOUT)
    try:
        for _ in range(300):
            try:
                request()
                break
            except (OSError, AssertionError):
                if server.poll() is not None:
                    raise RuntimeError('Diagnostic server failed; see server.log')
                time.sleep(.05)
        else:
            raise TimeoutError('Diagnostic server did not start')
        for transport, endpoint in [('http', hp), ('magiconion', gp)]:
            game_path = output / (transport + '-game.log')
            operator_path = output / (transport + '-operator.log')
            image_path = output / (transport + '.png')
            with game_path.open('w') as gamelog, operator_path.open('w') as operatorlog:
                game = subprocess.Popen([args.dotnet, dll('samples/Lumyte.Diagnostics.UI.Automation.Game.Sample'), transport, f'http://127.0.0.1:{endpoint}', args.gpu], cwd=root, env=env, stdout=gamelog, stderr=subprocess.STDOUT)
                try:
                    operator = subprocess.Popen([args.dotnet, dll('samples/Lumyte.Diagnostics.UI.Automation.Operator.Sample'), f'http://127.0.0.1:{hp}', str(image_path)], cwd=root, env=env, stdout=operatorlog, stderr=subprocess.STDOUT)
                    try:
                        operator.wait(timeout=75)
                    finally:
                        if operator.poll() is None:
                            operator.kill()
                            operator.wait()
                    assert operator.returncode == 0, operator_path.read_text()
                    game.wait(timeout=15)
                    assert game.returncode == 0, game_path.read_text()
                    text = game_path.read_text()
                    assert text.count('Save count:') == 2 and 'Save count: 2' in text
                    if args.gpu != 'none':
                        validate_png(image_path)
                    record = dict(transport=transport, serverPid=server.pid, gamePid=game.pid,
                                  operatorPid=operator.pid, saves=2, png=args.gpu != 'none', result='success')
                    assert len({server.pid, game.pid, operator.pid}) == 3
                    results.append(record)
                    print(json.dumps(record), flush=True)
                finally:
                    if game.poll() is None:
                        game.terminate()
                        game.wait(timeout=10)
    finally:
        server.terminate()
        try:
            server.wait(timeout=10)
        except subprocess.TimeoutExpired:
            server.kill()
            server.wait()
(output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
