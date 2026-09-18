"""Offline conversion of the pinned Khronos BoxVertexColors fixture, not a glTF loader.

Run with the directory containing BoxVertexColors.gltf and buffer.bin, then an
output model.json path. No network access or runtime loading is involved.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

source = Path(sys.argv[1])
expected = {
    'BoxVertexColors.gltf': '53d15f2357d410d0cc9e798065709054474db25ba52274f012bb7fb96a2eaddf',
    'buffer.bin': 'b508427bea091cc29cbe68d8b08e75db27e33559f72104a37343c7e808e72d26',
}
for name, digest in expected.items():
    if hashlib.sha256((source / name).read_bytes()).hexdigest() != digest:
        raise ValueError(f'{name} does not match the pinned Khronos fixture')
document = json.loads((source / 'BoxVertexColors.gltf').read_text())
buffer = (source / 'buffer.bin').read_bytes()

def accessor(index):
    item = document['accessors'][index]
    view = document['bufferViews'][item['bufferView']]
    assert view['buffer'] == 0 and 'sparse' not in item and not item.get('normalized', False)
    components = {'SCALAR': 1, 'VEC3': 3}[item['type']]
    component = {5123: 'H', 5126: 'f'}[item['componentType']]
    layout = struct.Struct('<' + component * components)
    stride = view.get('byteStride', layout.size)
    start = view.get('byteOffset', 0) + item.get('byteOffset', 0)
    return [list(layout.unpack_from(buffer, start + i * stride)) for i in range(item['count'])]

primitive = document['meshes'][0]['primitives'][0]
assert primitive['mode'] == 4 and document['nodes'] == [{'mesh': 0}]
attributes = primitive['attributes']
fixture = {
    'positions': accessor(attributes['POSITION']),
    'normals': accessor(attributes['NORMAL']),
    'colors': [color + [1] for color in accessor(attributes['COLOR_0'])],
    'indices': [value[0] for value in accessor(primitive['indices'])],
}
Path(sys.argv[2]).write_text(json.dumps(fixture, separators=(',', ':')) + '\n', encoding='utf-8')
