"""Verify three release artifacts offline in isolated temporary files.
Never launches host/installer, connects LCD/DSH, or installs dependencies.
Requires Windows, Python, and the Roslyn compiler used by build.cmd.
"""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = '1.0.6'
PLUGIN_VERSION = '0.1.2'
DSH_FLOOR = '>=0.2.1-rc.2'
CSC = Path(r'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe')
PLUGIN_FILES = {'package.json', 'dsh.plugin.json', 'cordis.patch.yml', 'README.md',
                'CONTRACTS.md', 'LICENSE', 'lib/index.js', 'lib/bridge.js'}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def run(args, cwd, env=None, timeout=120):
    p = subprocess.run(list(map(str, args)), cwd=cwd, env=env, capture_output=True, timeout=timeout)
    if p.returncode:
        raise RuntimeError(f'{args[0]} failed ({p.returncode}): ' + (p.stdout + p.stderr).decode(errors='replace')[-4000:])
    return p.stdout.decode(errors='replace')


def read_json(data):
    require(not data.startswith(b'\xef\xbb\xbf'), 'JSON must be UTF-8 without BOM')
    return json.loads(data.decode('utf-8'))


def archive_files(path, prefix='', allow_dot=False):
    """Reject duplicate/unsafe paths before extracting any files."""
    result = {}
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            name = info.filename.replace('\\', '/')
            if allow_dot and name.startswith('./'):
                name = name[2:]
            if info.is_dir():
                continue
            parts = PurePosixPath(name).parts
            require(name and not name.startswith('/') and ':' not in name and '..' not in parts,
                    f'Unsafe archive path: {name}')
            require(name.startswith(prefix), f'Unexpected archive root: {name}')
            relative = name[len(prefix):]
            require(relative and relative not in result, f'Duplicate archive path: {relative}')
            result[relative] = z.read(info)
    return result


def check_plugin(files):
    require(set(files) == PLUGIN_FILES, 'Plugin runtime file set differs from allowlist')
    package = read_json(files['package.json'])
    manifest = read_json(files['dsh.plugin.json'])
    require(package['name'] == 'dsh-gpulcd-status', 'Plugin name mismatch')
    require(manifest['id'] == 'dsh-external/dsh-gpulcd-status', 'Plugin id mismatch')
    for document in (package, manifest):
        require(document['version'] == PLUGIN_VERSION, 'Plugin version mismatch')
        require(document['main'] == './lib/index.js', 'Plugin entry mismatch')
        require(document['engines']['dsh'] == DSH_FLOOR, 'DSH compatibility mismatch')
    require(package['engines']['node'] == '>=24', 'Node compatibility mismatch')
    require(package['dsh']['bundle']['patch'] == 'cordis.patch.yml', 'Bundle patch mismatch')
    require('CONTRACTS.md' in package['files'], 'CONTRACTS not declared')
    require(b'id: dsh-gpulcd-status' in files['cordis.patch.yml'], 'Bundle registration absent')
    for name, data in files.items():
        source = ROOT / ('LICENSE' if name == 'LICENSE' else 'dsh-plugin/' + name)
        require(data == source.read_bytes(), f'Plugin/source mismatch: {name}')


def main():
    release = ROOT / 'release'
    artifacts = [f'GpuLcd-Setup-{VERSION}.exe', f'GpuLcd-{VERSION}-portable.zip',
                 f'dsh-gpulcd-status-{PLUGIN_VERSION}.zip']
    sums = (release / 'SHA256SUMS.txt').read_text(encoding='ascii').splitlines()
    require(len(sums) == 3, 'SHA256SUMS must contain exactly three lines')
    parsed = {}
    for line in sums:
        value, name = line.split('  ', 1)
        require(name not in parsed, 'Duplicate checksum filename')
        parsed[name] = value
    require(set(parsed) == set(artifacts), 'Checksum artifact names mismatch')
    for name in artifacts:
        data = (release / name).read_bytes()
        require(parsed[name] == digest(data), f'Checksum mismatch: {name}')
        print(f'PASS SHA256 {name}: {len(data)} bytes {parsed[name]}', flush=True)

    for path in ('AssemblyInfo.cs', 'installer/Setup.cs'):
        text = (ROOT / path).read_text(encoding='utf-8-sig')
        for attribute in ('AssemblyVersion', 'AssemblyFileVersion'):
            require(f'{attribute}("{VERSION}.0")' in text, f'Source version mismatch: {path}')
    script = (ROOT / 'package.ps1').read_bytes()
    script.decode('ascii')
    require(f"$ver = '{VERSION}'".encode() in script and
            f"$pluginVer = '{PLUGIN_VERSION}'".encode() in script, 'Packaging version mismatch')

    standalone = archive_files(release / artifacts[2], 'dsh-gpulcd-status/')
    check_plugin(standalone)
    portable = archive_files(release / artifacts[1], 'GpuLcd/')
    manifest = read_json(portable['bundle-manifest.json'])
    listed = {item['path'] for item in manifest}
    require(len(listed) == len(manifest), 'Duplicate manifest row')
    require(set(portable) == listed | {'bundle-manifest.json'}, 'Manifest does not cover exact payload')
    for item in manifest:
        data = portable[item['path']]
        require(len(data) == item['bytes'] and digest(data) == item['sha256'], f"Manifest mismatch: {item['path']}")
    print(f'PASS portable manifest: exact {len(manifest)} files, UTF-8 no BOM', flush=True)
    bundled = {name[len('dsh-plugin/'):]: data for name, data in portable.items() if name.startswith('dsh-plugin/')}
    require(bundled == standalone, 'Standalone and bundled plugin differ')
    print(f'PASS plugin {PLUGIN_VERSION}: {len(standalone)} files identical to source and portable (including LICENSE/CONTRACTS)', flush=True)
    for name in ('dswork.gif', 'dsask.gif', 'dsdone.gif'):
        relative = 'anims/' + name
        require(portable[relative] == (ROOT / relative).read_bytes(), f'DSH GIF mismatch: {name}')
        require(portable[relative][:6] in (b'GIF87a', b'GIF89a'), f'Not a GIF: {name}')
    template = 'integration/CodexLCD.template.html'
    require(portable[template] == (ROOT / template).read_bytes(), 'Lighting template differs from accepted source')
    require(b'(claude|codex|dsh)@' in portable[template] and b';client=' in portable[template], 'DSH source/receipt template absent')
    print('PASS three DSH GIFs and source-aware lighting template', flush=True)

    with tempfile.TemporaryDirectory(prefix='GpuLcd-package-') as temp:
        temp = Path(temp)
        app = temp / 'GpuLcd'
        for name, data in portable.items():
            target = app / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        resource_probe = temp / 'ReleaseResourceProbe.exe'
        run([CSC, '/nologo', '/target:exe', '/platform:x64', '/out:' + str(resource_probe),
             ROOT / 'tools/ReleaseResourceProbe.cs'], temp)
        payload = temp / 'installer-payload.zip'
        print(run([resource_probe, release / artifacts[0], 'payload.zip', payload, VERSION], temp), end='', flush=True)
        installer = archive_files(payload)
        require(installer == portable, 'Installer and portable differ (including manifest/plugin/UI host)')
        print(f'PASS installer = portable: all {len(portable)} files byte-identical; plugin identical in all three artifacts', flush=True)
        embedded = temp / 'embedded-ui.zip'
        print(run([resource_probe, app / 'GpuLcd.exe', 'GpuLcd.ui-dist.zip', embedded, VERSION], temp), end='', flush=True)
        ui = archive_files(embedded, allow_dot=True)
        dist = {p.relative_to(ROOT / 'ui/dist').as_posix(): p.read_bytes()
                for p in (ROOT / 'ui/dist').rglob('*') if p.is_file()}
        require(ui == dist and 'index.html' in ui, 'Actual embedded UI differs from freshly built ui/dist')
        require(any(name.endswith('.js') for name in ui), 'Embedded UI contains no JS')
        require(portable['GpuLcd.exe'] == (ROOT / 'GpuLcd.exe').read_bytes(), 'Packaged host differs from new build')
        print(f'PASS actual embedded UI: all {len(ui)} files identical to rebuilt ui/dist; zip SHA256={digest(embedded.read_bytes())}', flush=True)

        # Load host types/vendor libraries only, not Main/forms/devices.
        probe = app / 'PackageProbe.exe'
        run([CSC, '/nologo', '/target:exe', '/platform:x64', '/out:' + str(probe), ROOT / 'tests/PackageProbe.cs'], temp)
        env = os.environ.copy()
        env['PATH'] = str(Path(env['WINDIR']) / 'System32') + ';' + env['WINDIR']
        print(run([probe], temp, env), end='', flush=True)

        # Conversion in TEMP, without AppData cache or LCD upload.
        work = temp / 'conversion'
        work.mkdir()
        source = app / 'vendor/iGameCenter/SAVIConverter'
        shutil.copytree(source / 'tools', work / 'tools')
        shutil.copytree(source / 'configs', work / 'configs')
        ffmpeg = work / 'tools/ffmpeg.exe'
        run([ffmpeg, '-y', '-i', app / 'anims/dsdone.gif', '-t', '0.2', '-vf',
             'fps=10,scale=480:128,split[a][b];[a]palettegen[p];[b][p]paletteuse', work / 'input.gif'], work, env)
        run([work / 'tools/SAVI.exe', '-input:.\\input.avi', '-cfg:.\\configs\\giftosavi_singledisplay.xml'], work, env)
        savi = work / 'Bin/input.savi'
        require(savi.exists() and savi.stat().st_size > 1024, 'No valid conversion output')
        print(f'PASS bundled FFmpeg + SAVI + JPEG conversion: {savi.stat().st_size} bytes; no LCD upload', flush=True)
    print('PASS release verification complete; no host/installer entry point or live integration executed', flush=True)


if __name__ == '__main__':
    main()
