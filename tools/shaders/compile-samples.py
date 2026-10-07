#!/usr/bin/env python3
"""Regenerate the checked-in WGSL from the Slang sources using the mise-pinned compiler."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--compiler', default='slangc')
parser.add_argument('--check', action='store_true', help='Fail if generated WGSL differs without changing files.')
args = parser.parse_args()
compiler = shutil.which(args.compiler)
if compiler is None:
    parser.error('slangc not found; activate the mise environment or pass --compiler PATH')
root = Path(__file__).resolve().parents[2]
changed = False
with tempfile.TemporaryDirectory() as directory:
    for source in sorted((root / 'samples/Wgpu.Headless/Shaders').glob('*.slang')):
        generated = Path(directory) / (source.stem + '.wgsl')
        subprocess.run([compiler, str(source), '-target', 'wgsl', '-matrix-layout-row-major', '-o', str(generated)], check=True)
        target = source.with_suffix('.wgsl')
        # Normalize line endings for Windows checkouts.
        text = generated.read_text(encoding='utf-8').rstrip() + '\n'
        if args.check:
            if not target.exists() or target.read_text(encoding='utf-8') != text:
                print(f'Stale WGSL: {target.relative_to(root)}', file=sys.stderr)
                changed = True
        else:
            target.write_text(text, encoding='utf-8', newline='\n')
sys.exit(1 if changed else 0)
