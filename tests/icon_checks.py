"""Check committed icon sizes and native packaging on each CI host (stdlib only)."""
import argparse
import hashlib
import io
import plistlib
import struct
import subprocess
import tarfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT/'desktop/Assets'
SIZES = (16,24,32,48,64,128,256,512)
def png_size(data):
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    return struct.unpack_from('>II', data, 16)
def ico_images(data):
    reserved,kind,count = struct.unpack_from('<HHH', data)
    assert reserved == 0 and kind == 1
    result = {}
    for i in range(count):
        w,h,_,_,_,_,length,offset = struct.unpack_from('<BBBBHHII',data,6+i*16)
        result[(w or 256,h or 256)] = data[offset:offset+length]
    return result
def pe_icons(path):
    data = path.read_bytes(); pe = struct.unpack_from('<I',data,60)[0]
    assert data[pe:pe+4] == b'PE\0\0'
    count = struct.unpack_from('<H',data,pe+6)[0]; optional = pe+24
    opt_size = struct.unpack_from('<H',data,pe+20)[0]
    directories = optional + (112 if struct.unpack_from('<H',data,optional)[0] == 0x20b else 96)
    rva = struct.unpack_from('<I',data,directories+16)[0]
    sections = []
    for i in range(count):
        offset = optional+opt_size+i*40
        size,va,raw_size,raw = struct.unpack_from('<IIII',data,offset+8)
        sections.append((va,max(size,raw_size),raw))
    def address(value):
        for va,size,raw in sections:
            if va <= value < va+size:return raw+value-va
        raise AssertionError('Invalid resource address')
    base = address(rva)
    def entries(offset):
        names,ids = struct.unpack_from('<HH',data,base+offset+12)
        return [struct.unpack_from('<II',data,base+offset+16+i*8) for i in range(names+ids)]
    def leaves(offset):
        for _,target in entries(offset):
            if target & 0x80000000:yield from leaves(target & 0x7fffffff)
            else:
                start,size = struct.unpack_from('<II',data,base+target)
                yield data[address(start):address(start)+size]
    icons = []
    for kind,target in entries(0):
        if kind == 3:icons.extend(leaves(target & 0x7fffffff))
    return icons

def main(rid):
    for size in SIZES:assert png_size((ASSETS/f'icon-{size}.png').read_bytes()) == (size,size)
    assert png_size((ASSETS/'icon-small-128.png').read_bytes()) == (128,128)
    ico = ico_images((ASSETS/'app.ico').read_bytes())
    assert set(ico) == {(s,s) for s in SIZES if s <= 256}
    icns = (ASSETS/'app.icns').read_bytes(); assert icns[:4] == b'icns' and struct.unpack_from('>I',icns,4)[0] == len(icns)
    offset = 8; types = set()
    while offset < len(icns):
        kind,size = struct.unpack_from('>4sI',icns,offset); assert size > 8
        types.add(kind); offset += size
    assert offset == len(icns) and {b'ic07',b'ic08',b'ic09',b'ic10'} <= types
    bundle = ROOT/'dist/native'/rid
    if rid.startswith('win'):
        embedded = pe_icons(bundle/'PickerHub/PickerHub.exe')
        for image in ico.values():assert image in embedded, 'Executable is missing an ICO resolution'
    elif rid.startswith('osx'):
        app = bundle/'PickerHub.app/Contents'
        info = plistlib.loads((app/'Info.plist').read_bytes())
        assert info['CFBundleIconFile'] == 'app.icns'
        assert (app/'Resources'/info['CFBundleIconFile']).read_bytes() == icns
    else:
        assert (bundle/'PickerHub/pickerhub.png').read_bytes() == (ASSETS/'icon-256.png').read_bytes()
        version = ET.parse(ROOT/'desktop/PickerHub.csproj').findtext('PropertyGroup/Version')
        contents = subprocess.check_output(['dpkg-deb','--fsys-tarfile',str(ROOT/f'dist/native/pickerhub_{version}_amd64.deb')])
        with tarfile.open(fileobj=io.BytesIO(contents)) as archive:
            entry = archive.extractfile('./usr/share/applications/pickerhub.desktop').read().decode()
            assert 'Icon=pickerhub\n' in entry and 'StartupWMClass=pickerhub\n' in entry
            for size in SIZES:
                assert archive.extractfile(f'./usr/share/icons/hicolor/{size}x{size}/apps/pickerhub.png').read() == (ASSETS/f'icon-{size}.png').read_bytes()
    print(f'PASS: {rid} native icon packaging and all source resolutions')
if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--rid',required=True)
    main(parser.parse_args().rid)
