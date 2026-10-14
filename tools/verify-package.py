"""Verify both release payloads and conversion in an isolated temporary directory.

Does not launch the installer, open the LCD serial port, or change user settings.
Requires Windows, Python, and the same Roslyn compiler as build.cmd.
"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = '1.0.2'
CSC = Path(r'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe')


def run(args, cwd, env=None, timeout=120):
    p = subprocess.run(list(map(str, args)), cwd=cwd, env=env, capture_output=True, timeout=timeout)
    if p.returncode:
        raise RuntimeError(f'{args[0]} failed ({p.returncode}): ' + (p.stdout + p.stderr).decode(errors='replace')[-4000:])
    return p.stdout.decode(errors='replace')


def main():
    release = ROOT / 'release'
    with tempfile.TemporaryDirectory(prefix='GpuLcd-package-') as temp:
        temp = Path(temp)
        with zipfile.ZipFile(release / f'GpuLcd-{VERSION}-portable.zip') as z:
            for info in z.infolist():
                target = (temp / info.filename).resolve()
                if not target.is_relative_to(temp):
                    raise ValueError('Unsafe archive path')
            z.extractall(temp)
        app = temp / 'GpuLcd'
        manifest = json.loads((app / 'bundle-manifest.json').read_text(encoding='utf-8-sig'))
        for item in manifest:
            p = app / item['path']
            assert p.stat().st_size == item['bytes'], item['path']
            assert hashlib.sha256(p.read_bytes()).hexdigest() == item['sha256'], item['path']
        print(f'PASS portable manifest: {len(manifest)} files', flush=True)

        probe = app / 'PackageProbe.exe'
        run([CSC, '/nologo', '/target:exe', '/platform:x64', '/out:' + str(probe), ROOT / 'tests/PackageProbe.cs'], temp)
        env = os.environ.copy()
        env['PATH'] = str(Path(env['WINDIR']) / 'System32') + ';' + env['WINDIR']
        print(run([probe], temp, env), end='', flush=True)
        payload = temp / 'installer-payload.zip'
        print(run([probe, release / f'GpuLcd-Setup-{VERSION}.exe', payload], temp), end='', flush=True)
        with zipfile.ZipFile(payload) as z:
            names = {info.filename.replace('\\', '/'): info for info in z.infolist() if not info.is_dir()}
            assert set(names) == {item['path'] for item in manifest} | {'bundle-manifest.json'}
            for item in manifest:
                assert hashlib.sha256(z.read(names[item['path']])).hexdigest() == item['sha256'], item['path']
        print('PASS installer and portable contain identical files', flush=True)

        # Reproduce the application's conversion sequence without its AppData cache.
        work = temp / 'conversion'
        work.mkdir()
        source = app / 'vendor/iGameCenter/SAVIConverter'
        shutil.copytree(source / 'tools', work / 'tools')
        shutil.copytree(source / 'configs', work / 'configs')
        ffmpeg = work / 'tools/ffmpeg.exe'
        run([ffmpeg, '-y', '-i', app / 'anims/ccdone.gif', '-t', '0.2', '-vf',
             'fps=10,scale=480:128,split[a][b];[a]palettegen[p];[b][p]paletteuse', work / 'input.gif'], work, env)
        run([work / 'tools/SAVI.exe', '-input:.\\input.avi', '-cfg:.\\configs\\giftosavi_singledisplay.xml'], work, env)
        savi = work / 'Bin/input.savi'
        assert savi.exists() and savi.stat().st_size > 1024, 'No valid conversion output'
        print(f'PASS bundled FFmpeg + SAVI + JPEG conversion: {savi.stat().st_size} bytes', flush=True)


if __name__ == '__main__':
    main()
